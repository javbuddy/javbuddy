using Javbuddy.Models;

namespace Javbuddy.Services.R18Dev;

public interface IR18DevDumpService
{
    /// <summary>Whether an imported dump is on disk.</summary>
    bool IsDumpAvailable();

    /// <summary>Saves the r18.dev tile's form. Switching the source from enabled to disabled also deletes the
    /// imported dump and clears the import bookkeeping, which would otherwise describe a dump that is gone.</summary>
    Task SaveAsync(R18DevSettings settings, bool wasEnabled, CancellationToken ct = default);

    /// <summary>Deletes the imported dump, clears the import bookkeeping and saves the settings.</summary>
    Task DeleteDumpAsync(R18DevSettings settings, CancellationToken ct = default);
}

/// <summary>What Settings &gt; Metadata's r18.dev tile does with the imported dump: the file deletion
/// (<see cref="IR18DevDumpStore"/>) together with the settings bookkeeping that records the import.</summary>
public class R18DevDumpService(IR18DevDumpStore dumpStore, IR18DevSettingsService settingsService) : IR18DevDumpService
{
    public bool IsDumpAvailable() => dumpStore.IsAvailable();

    public async Task SaveAsync(R18DevSettings settings, bool wasEnabled, CancellationToken ct = default)
    {
        if (wasEnabled && !settings.Enabled)
        {
            dumpStore.DeleteDump();
            ClearImportBookkeeping(settings);
        }

        await settingsService.SaveAsync(settings, ct);
    }

    public async Task DeleteDumpAsync(R18DevSettings settings, CancellationToken ct = default)
    {
        dumpStore.DeleteDump();
        ClearImportBookkeeping(settings);
        await settingsService.SaveAsync(settings, ct);
    }

    private static void ClearImportBookkeeping(R18DevSettings settings)
    {
        settings.LastImportedAt = null;
        settings.LastImportSourceDate = null;
        settings.LastImportSummary = null;
    }
}
