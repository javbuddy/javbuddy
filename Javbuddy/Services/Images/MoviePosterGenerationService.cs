using Javbuddy.Data;
using Javbuddy.Services.Common;
using Javbuddy.Services.Ffmpeg;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Services.Movies;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;

namespace Javbuddy.Services.Images;

public interface IMoviePosterGenerationService
{
    /// <summary>Samples frames from the movie's video, picks the sharpest well-exposed ones and writes "poster.webp"
    /// (a 2:3 crop) and "fanart.webp" (the full frame) into its folder — only the ones that don't exist yet, never
    /// overwriting an image. Fails when the movie has no local video or already has both. Frames of side-by-side VR video
    /// are cropped to the left eye. With <paramref name="respectRemoteArtwork"/>, a poster (or fanart) isn't generated for a
    /// movie whose metadata already has a cover (or backdrop) URL, so a real remote cover isn't shadowed by a video frame.</summary>
    Task<OperationResult> GenerateAsync(int movieId, bool respectRemoteArtwork = false, CancellationToken ct = default);
}

/// <summary>For a movie that came without artwork (no scraper has covers for most uncensored releases).</summary>
public class MoviePosterGenerationService(
    IDbContextFactory<AppDbContext> dbFactory,
    ILocalLibraryClient localLibraryClient,
    IFfmpegClient ffmpegClient,
    ILogger<MoviePosterGenerationService> logger) : IMoviePosterGenerationService
{
    internal const int SampleCount = 9;
    private const double PosterAspectRatio = 2.0 / 3.0;
    private const int WebpQuality = 92;

    public async Task<OperationResult> GenerateAsync(int movieId, bool respectRemoteArtwork = false, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var movie = await db.Movies.AsNoTracking().FirstOrDefaultAsync(m => m.Id == movieId, ct);
        if (movie is null || string.IsNullOrWhiteSpace(movie.Code))
        {
            return OperationResult.Fail("Movie not found.");
        }

        var folder = await localLibraryClient.ResolveMovieFolderPathAsync(movie.Code, ct);
        if (folder is null)
        {
            return OperationResult.Fail("No local folder found for this movie.");
        }

        var needPoster = !HasImage(folder, "poster") && !HasImage(folder, "folder");
        var needFanart = !HasImage(folder, "fanart");
        if (respectRemoteArtwork)
        {
            needPoster &= string.IsNullOrWhiteSpace(movie.MetaCoverUrl);
            needFanart &= string.IsNullOrWhiteSpace(movie.MetaBackdropUrl);
        }
        if (!needPoster && !needFanart)
        {
            return OperationResult.Fail("The movie already has a poster and fanart.");
        }

        var video = MovieVersionParser.FindVideoFiles(folder)
            .OrderByDescending(f => string.Equals(Path.GetFileNameWithoutExtension(f), movie.Code, StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();
        if (video is null || movie.MediaDurationSeconds is not > 0)
        {
            return OperationResult.Fail("The movie has no scanned local video to take frames from. Rescan it first.");
        }

        var scratch = Path.Combine(Path.GetTempPath(), "javbuddy-frames-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            var candidates = new List<(string Path, double Score)>();
            string? firstError = null;
            for (var i = 1; i <= SampleCount; i++)
            {
                var framePath = Path.Combine(scratch, $"{i}.webp");
                var extracted = await ffmpegClient.ExtractFrameWebpAsync(video, movie.MediaDurationSeconds.Value * i / (SampleCount + 1), movie.IsSideBySideVr, framePath, ct);
                if (!extracted.Success || !File.Exists(framePath) || new FileInfo(framePath).Length == 0)
                {
                    firstError ??= extracted.ErrorMessage ?? "ffmpeg wrote no frame at that point of the video";
                    continue;
                }

                using var bitmap = SKBitmap.Decode(framePath);
                if (bitmap is not null)
                {
                    candidates.Add((framePath, FrameScorer.Score(bitmap)));
                }
                else
                {
                    firstError ??= "the extracted frame couldn't be decoded";
                }
            }

            if (candidates.Count == 0)
            {
                logger.LogWarning("No frame could be extracted from {Video} for {Code}: {Error}", video, movie.Code, firstError);
                return OperationResult.Fail($"Couldn't extract any frames from the video ({firstError}).");
            }

            var ranked = candidates.OrderByDescending(c => c.Score).ToList();
            var posterSource = ranked[0].Path;
            var fanartSource = (ranked.Count > 1 ? ranked[1] : ranked[0]).Path;

            // Everything that can fail on a bad frame happens before the first file is written, so a failure never
            // leaves a lone fanart.webp behind (which would block generating it later).
            var poster = needPoster ? CropToPoster(posterSource) : null;
            if (needPoster && poster is null)
            {
                return OperationResult.Fail("The video's frames are too small to make a poster from.");
            }

            var fanart = needFanart ? EncodeWebp(fanartSource) : null;
            if (needFanart && fanart is null)
            {
                return OperationResult.Fail("Couldn't convert the extracted frame to WebP.");
            }

            if (fanart is not null) WriteFile(Path.Combine(folder, "fanart.webp"), fanart);
            if (poster is not null) WriteFile(Path.Combine(folder, "poster.webp"), poster);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Failed to write generated artwork for {Code}", movie.Code);
            return OperationResult.Fail("Couldn't write into the movie's folder.");
        }
        finally
        {
            try { Directory.Delete(scratch, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        await localLibraryClient.SyncImagesSignatureAsync(movieId, ct);
        return OperationResult.Ok();
    }

    /// <summary>The frame re-encoded as WebP bytes, or null when it can't be decoded or encoded.</summary>
    private static byte[]? EncodeWebp(string sourceImage)
    {
        using var frame = SKBitmap.Decode(sourceImage);
        if (frame is null) return null;

        using var data = frame.Encode(SKEncodedImageFormat.Webp, WebpQuality);
        return data?.ToArray();
    }

    /// <summary>A centered 2:3 crop of the frame as WebP bytes, or null when the frame is too small to crop or encode.</summary>
    private static byte[]? CropToPoster(string sourceImage)
    {
        using var frame = SKBitmap.Decode(sourceImage);
        if (frame is null) return null;

        var cropWidth = Math.Min(frame.Width, (int)(frame.Height * PosterAspectRatio));
        if (cropWidth < 1 || frame.Height < 1) return null;

        var left = (frame.Width - cropWidth) / 2;
        var rect = new SKRectI(left, 0, left + cropWidth, frame.Height);

        using var poster = new SKBitmap(rect.Width, rect.Height);
        using (var canvas = new SKCanvas(poster))
        {
            canvas.DrawBitmap(frame, rect, new SKRect(0, 0, rect.Width, rect.Height), SKSamplingOptions.Default);
        }

        using var data = poster.Encode(SKEncodedImageFormat.Webp, WebpQuality);
        return data?.ToArray();
    }

    /// <summary>Creates <paramref name="target"/> through a sibling temp file, so a media server never reads it half
    /// written and a failure leaves neither a truncated image (which would count as "already has one") nor a stray
    /// temp file. Never overwrites.</summary>
    private static void WriteFile(string target, byte[] content)
    {
        var temp = target + ".tmp";
        try
        {
            File.WriteAllBytes(temp, content);
            File.Move(temp, target, overwrite: false);
        }
        finally
        {
            try { File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static bool HasImage(string folder, string stem) =>
        MediaFileTypes.ImageExtensions.Any(ext => File.Exists(Path.Combine(folder, stem + ext)));
}
