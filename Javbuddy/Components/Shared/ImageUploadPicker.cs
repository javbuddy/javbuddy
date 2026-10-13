using Javbuddy.Services.Images;
using Microsoft.AspNetCore.Components.Forms;

namespace Javbuddy.Components.Shared;

/// <summary>The checks the multi-image upload modals run on a picked or dropped batch: at most the batch
/// limit, JPEG/PNG/WebP only, each within the size limit. Either every file is accepted or none is, with
/// the message to show.</summary>
public static class ImageUploadPicker
{
    public static (List<IBrowserFile> Files, string? Error) Pick(InputFileChangeEventArgs e, ImageUploadSettings settings, string noun)
    {
        IReadOnlyList<IBrowserFile> picked;
        try
        {
            picked = e.GetMultipleFiles(settings.MaxBatchFiles);
        }
        catch (InvalidOperationException)
        {
            return ([], $"You can select at most {settings.MaxBatchFiles} {noun} at a time.");
        }

        foreach (var file in picked)
        {
            if (Path.GetExtension(file.Name).ToLowerInvariant() is not (".jpg" or ".jpeg" or ".png" or ".webp"))
            {
                return ([], "Only JPEG, PNG, and WebP images are supported.");
            }
            if (file.Size > settings.MaxSizeBytes)
            {
                return ([], $"Each image must be no larger than {settings.MaxSizeMb} MB.");
            }
        }
        return ([.. picked], null);
    }
}
