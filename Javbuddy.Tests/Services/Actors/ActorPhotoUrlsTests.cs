using Javbuddy.Services.Actors;

namespace Javbuddy.Tests.Services.Actors;

public class ActorPhotoUrlsTests
{
    [Fact]
    public void Url_WithAVersion_CarriesItAsV()
    {
        Assert.Equal("/actor-photo/12/thumb?v=0a1b2c3d", ActorPhotoUrls.Url(12, "thumb", "0a1b2c3d"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Url_WithoutAVersion_IsThePlainPath(string? version)
    {
        Assert.Equal("/actor-photo/12/full", ActorPhotoUrls.Url(12, "full", version));
    }

    [Fact]
    public void Version_IsAShortTokenOfTheStorageId_DifferentPerPhoto()
    {
        var a = ActorPhotoUrls.Version(Guid.Parse("0a1b2c3d-4e5f-6789-abcd-ef0123456789"));
        var b = ActorPhotoUrls.Version(Guid.Parse("ffffffff-4e5f-6789-abcd-ef0123456789"));

        Assert.Equal("0a1b2c3d", a);
        Assert.NotEqual(a, b);
    }
}
