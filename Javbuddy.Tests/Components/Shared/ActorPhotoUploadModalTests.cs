using Bunit;
using Javbuddy.Components.Shared;
using Javbuddy.Services.Actors;
using Javbuddy.Services.Images;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Shared;

public class ActorPhotoUploadModalTests : BunitContext
{
    [Fact]
    public void ActorPhotoUploadModal_RendersHidden_WhenShowIsFalse()
    {
        var photoService = Substitute.For<IActorPhotoService>();
        Services.AddSingleton(photoService);

        var cut = Render<ActorPhotoUploadModal>(p => p
            .Add(m => m.Show, false)
            .Add(m => m.ActorId, 1)
            .Add(m => m.ActorName, "Yua Mikami"));

        Assert.Empty(cut.FindAll(".actor-photo-upload-backdrop"));
    }

    [Fact]
    public void ActorPhotoUploadModal_RendersDefaultMaxSize_WhenNotConfigured()
    {
        var photoService = Substitute.For<IActorPhotoService>();
        Services.AddSingleton(photoService);

        var cut = Render<ActorPhotoUploadModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.ActorId, 1)
            .Add(m => m.ActorName, "Yua Mikami"));

        Assert.Contains("up to 20 MB per image", cut.Markup);
    }

    [Fact]
    public void ActorPhotoUploadModal_RendersCustomMaxSize_WhenConfigured()
    {
        var photoService = Substitute.For<IActorPhotoService>();
        Services.AddSingleton(photoService);
        Services.AddSingleton(new ImageUploadSettings(35));

        var cut = Render<ActorPhotoUploadModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.ActorId, 1)
            .Add(m => m.ActorName, "Yua Mikami"));

        Assert.Contains("up to 35 MB per image", cut.Markup);
    }

    [Fact]
    public void ActorPhotoUploadModal_RendersDefaultMaxBatchFiles_WhenNotConfigured()
    {
        var photoService = Substitute.For<IActorPhotoService>();
        Services.AddSingleton(photoService);

        var cut = Render<ActorPhotoUploadModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.ActorId, 1)
            .Add(m => m.ActorName, "Yua Mikami"));

        Assert.Contains("up to 500 photos per batch", cut.Markup);
    }

    [Fact]
    public void ActorPhotoUploadModal_RendersCustomMaxBatchFiles_WhenConfigured()
    {
        var photoService = Substitute.For<IActorPhotoService>();
        Services.AddSingleton(photoService);
        Services.AddSingleton(new ImageUploadSettings(20, 150));

        var cut = Render<ActorPhotoUploadModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.ActorId, 1)
            .Add(m => m.ActorName, "Yua Mikami"));

        Assert.Contains("up to 150 photos per batch", cut.Markup);
    }

    [Fact]
    public void ActorPhotoUploadModal_ShowsError_WhenSelectedFilesExceedMaxBatchFiles()
    {
        var photoService = Substitute.For<IActorPhotoService>();
        Services.AddSingleton(photoService);
        Services.AddSingleton(new ImageUploadSettings(20, 1));

        var cut = Render<ActorPhotoUploadModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.ActorId, 1)
            .Add(m => m.ActorName, "Yua Mikami"));

        var inputFile = cut.FindComponent<Microsoft.AspNetCore.Components.Forms.InputFile>();
        var file1 = InputFileContent.CreateFromText("abc", "photo1.jpg");
        var file2 = InputFileContent.CreateFromText("xyz", "photo2.jpg");

        inputFile.UploadFiles(file1, file2);

        Assert.Contains("You can select at most 1 photos at a time.", cut.Markup);
    }
}
