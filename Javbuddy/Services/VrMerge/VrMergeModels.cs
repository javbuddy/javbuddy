namespace Javbuddy.Services.VrMerge;

/// <summary>One detected part, probed, carrying the chapter title it will receive at its current
/// position — recomputed by the wizard state whenever the user reorders or excludes a part.</summary>
public record VrMergeCandidatePart
{
    public required string Path { get; init; }
    public required string Name { get; init; }
    public required long SizeBytes { get; init; }
    public required double DurationSeconds { get; init; }
    public string? VideoCodec { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public double? FrameRate { get; init; }
    public string? AudioCodec { get; init; }
    public int? AudioChannels { get; init; }
    public required string ChapterTitle { get; init; }
}

/// <summary>Result of DetectAsync: the parts found in merge order, plus any stream-mismatch warnings
/// (surfaced in the wizard modal, never blocking — the user decides whether to proceed).</summary>
public record VrMergeCandidate
{
    public required string FolderPath { get; init; }
    public required string CodeBase { get; init; }
    public required IReadOnlyList<VrMergeCandidatePart> Parts { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public record VrMergeRequest
{
    public required string FolderPath { get; init; }
    public required string CodeBase { get; init; }
    public required IReadOnlyList<VrMergeCandidatePart> OrderedParts { get; init; }
}

public record VrMergeResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public string? MergedFilePath { get; init; }

    public static VrMergeResult Ok(string mergedFilePath) => new() { Success = true, MergedFilePath = mergedFilePath };
    public static VrMergeResult Failed(string message) => new() { Success = false, ErrorMessage = message };
}
