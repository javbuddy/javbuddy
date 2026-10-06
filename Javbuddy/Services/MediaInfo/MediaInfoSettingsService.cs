using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.MediaInfo;

public interface IMediaInfoSettingsService
{
    Task<MediaInfoSettings> GetAsync(CancellationToken ct = default);
    Task SaveAsync(MediaInfoSettings settings, CancellationToken ct = default);
}

/// <summary>Settings &gt; Metadata's MediaInfo tile: a single-row upsert onto <see cref="MediaInfoSettings"/>.</summary>
public class MediaInfoSettingsService(IDbContextFactory<AppDbContext> dbFactory) : IMediaInfoSettingsService
{
    private readonly SingleRowSettingsRepository<MediaInfoSettings> repository = new(dbFactory, db => db.MediaInfoSettings, x => x.Id,
        (existing, form) => existing.Enabled = form.Enabled);

    public Task<MediaInfoSettings> GetAsync(CancellationToken ct = default) => repository.GetAsync(() => new MediaInfoSettings(), ct);

    public Task SaveAsync(MediaInfoSettings settings, CancellationToken ct = default) => repository.SaveAsync(settings, ct);
}
