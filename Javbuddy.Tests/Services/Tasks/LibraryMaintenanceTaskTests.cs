using Javbuddy.Models;
using Javbuddy.Services.Images;
using Javbuddy.Services.Infrastructure;
using Javbuddy.Services.Tasks;
using Javbuddy.Tests.TestSupport;
using SkiaSharp;

namespace Javbuddy.Tests.Services.Tasks;

public class LibraryMaintenanceTaskTests
{
    [Fact]
    public async Task RunAsync_DeletesAnOldUnreferencedOriginal_ButKeepsReferencedAndNewOnes()
    {
        // An original no ActorImage or ActorPhoto row references is deleted once a
        // day old; a new one may belong to a save that hasn't written its row yet.
        using var factory = new TestDbContextFactory();
        var stores = new InMemoryObjectStoreProvider();
        var objects = stores.Get(ObjectStoreArea.ActorImages);
        // Its own staging folder: the constructor purges it, and nothing here stages.
        var dataStore = new ActorImageDataStore(stores, Path.Combine(Path.GetTempPath(), $"javbuddy-test-staging-{Guid.NewGuid():N}"));

        var imageSourceId = Guid.NewGuid();
        var photoSourceId = Guid.NewGuid();
        var oldOrphanId = Guid.NewGuid();
        var newOrphanId = Guid.NewGuid();
        var keys = new Dictionary<Guid, string>();
        foreach (var id in new[] { imageSourceId, photoSourceId, oldOrphanId, newOrphanId })
        {
            await dataStore.WriteAsync(id, CreatePngBytes());
            keys[id] = Assert.Single(objects.Keys, k => k.Contains(id.ToString("N"), StringComparison.Ordinal));
        }
        var twoDaysAgo = DateTimeOffset.UtcNow.AddDays(-2);
        objects.SetLastModified(keys[imageSourceId], twoDaysAgo);
        objects.SetLastModified(keys[photoSourceId], twoDaysAgo);
        objects.SetLastModified(keys[oldOrphanId], twoDaysAgo);

        using (var db = factory.CreateDbContext())
        {
            var actor = new Actor { FirstName = "Someone" };
            db.Actors.Add(actor);
            db.SaveChanges();
            db.ActorImages.Add(new ActorImage { ActorId = actor.Id, SourceMovieCode = "ABC-123", Variant = "full", StorageId = Guid.NewGuid(), SourceStorageId = imageSourceId, SourceExtension = ".png" });
            db.ActorPhotos.Add(new ActorPhoto { ActorId = actor.Id, ThumbStorageId = Guid.NewGuid(), FullStorageId = Guid.NewGuid(), SourceStorageId = photoSourceId, SourceExtension = ".png" });
            db.SaveChanges();
        }

        var summary = await new LibraryMaintenanceTask(factory, dataStore).RunAsync(CancellationToken.None, new Progress<TaskProgress>());

        Assert.True(await dataStore.ExistsAsync(imageSourceId, ".png"), "an original an actor image references must be kept");
        Assert.True(await dataStore.ExistsAsync(photoSourceId, ".png"), "an original an actor photo references must be kept");
        Assert.True(await dataStore.ExistsAsync(newOrphanId, ".png"), "a new unreferenced original may be an in-flight save and must be kept");
        Assert.False(await dataStore.ExistsAsync(oldOrphanId, ".png"), "an old unreferenced original should be deleted");
        Assert.Equal("checked 4 actor image originals, deleted 1 unreferenced", summary);
    }

    private static byte[] CreatePngBytes()
    {
        using var bitmap = new SKBitmap(1, 1);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
