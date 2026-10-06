using Javbuddy.Services.Movies;
using NSubstitute;

namespace Javbuddy.Tests.TestSupport;

/// <summary>IMovieStreamService stand-ins for component tests that render the player.</summary>
public static class StreamServiceStubs
{
    /// <summary>Every movie has a local video file, so the player always gets a stream.</summary>
    public static IMovieStreamService AllPlayable()
    {
        var streams = Substitute.For<IMovieStreamService>();
        streams.GetMainFilePathAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => $"/library/movie-{call.Arg<int>()}.mp4");
        streams.GetFilePathAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => $"/library/movie-{call.ArgAt<int>(0)}-{call.ArgAt<int>(1)}.mp4");
        return streams;
    }
}
