using Javbuddy.Components.Shared;
using Javbuddy.Services.Images;
using Microsoft.AspNetCore.Components.Forms;
using NSubstitute;

namespace Javbuddy.Tests.Components.Shared;

public class ImageUploadPickerTests
{
    private static readonly ImageUploadSettings Settings = new(MaxSizeMb: 1, MaxBatchFiles: 2);

    private static IBrowserFile File(string name, long size = 100)
    {
        var file = Substitute.For<IBrowserFile>();
        file.Name.Returns(name);
        file.Size.Returns(size);
        return file;
    }

    private static InputFileChangeEventArgs Args(params IBrowserFile[] files) => new(files);

    [Fact]
    public void Pick_AcceptsSupportedImagesWithinTheLimits()
    {
        var (files, error) = ImageUploadPicker.Pick(Args(File("a.JPG"), File("b.webp")), Settings, "photos");

        Assert.Null(error);
        Assert.Equal(2, files.Count);
    }

    [Fact]
    public void Pick_RejectsMoreThanTheBatchLimit_NamingTheNoun()
    {
        var (files, error) = ImageUploadPicker.Pick(Args(File("a.jpg"), File("b.jpg"), File("c.jpg")), Settings, "photos");

        Assert.Empty(files);
        Assert.Equal("You can select at most 2 photos at a time.", error);
    }

    [Fact]
    public void Pick_RejectsUnsupportedExtensions_WithoutKeepingTheOthers()
    {
        var (files, error) = ImageUploadPicker.Pick(Args(File("a.jpg"), File("b.gif")), Settings, "images");

        Assert.Empty(files);
        Assert.Equal("Only JPEG, PNG, and WebP images are supported.", error);
    }

    [Fact]
    public void Pick_RejectsOversizedFiles()
    {
        var (files, error) = ImageUploadPicker.Pick(Args(File("a.jpg", size: 2 * 1024 * 1024)), Settings, "images");

        Assert.Empty(files);
        Assert.Equal("Each image must be no larger than 1 MB.", error);
    }
}
