namespace Javbuddy.Services.Javinizer;

/// <summary>Movie Detail's "Refresh with Javinizer" field picker — which of a fresh
/// javinizer-go scrape's fields to apply onto the movie. Mirrors WarashiImportOptions' shape
/// (Services/Warashi/WarashiModels.cs) for the actor-side metadata-review modal.</summary>
public class MovieMetadataImportOptions
{
    public bool ImportTitle { get; set; }
    public bool ImportOriginalTitle { get; set; }
    public bool ImportReleaseDate { get; set; }
    public bool ImportStudio { get; set; }
    public bool ImportLabel { get; set; }
    public bool ImportSeries { get; set; }
    public bool ImportDirector { get; set; }
    public bool ImportRuntime { get; set; }
    public bool ImportDescription { get; set; }
    public bool ImportActresses { get; set; }
    public bool ImportGenres { get; set; }
    public bool ImportCover { get; set; }

    public int SelectedCount(MovieViewDto? scraped)
    {
        if (scraped is null) return 0;

        var count = 0;
        if (ImportTitle && !string.IsNullOrWhiteSpace(MovieMetadataMapper.ResolveTitle(scraped))) count++;
        if (ImportOriginalTitle && !string.IsNullOrWhiteSpace(scraped.OriginalTitle)) count++;
        if (ImportReleaseDate && scraped.ReleaseDate.HasValue) count++;
        if (ImportStudio && !string.IsNullOrWhiteSpace(scraped.Maker)) count++;
        if (ImportLabel && !string.IsNullOrWhiteSpace(scraped.Label)) count++;
        if (ImportSeries && !string.IsNullOrWhiteSpace(scraped.Series)) count++;
        if (ImportDirector && !string.IsNullOrWhiteSpace(scraped.Director)) count++;
        if (ImportRuntime && scraped.Runtime.HasValue) count++;
        if (ImportDescription && !string.IsNullOrWhiteSpace(scraped.Description)) count++;
        if (ImportActresses && scraped.Actresses is { Count: > 0 }) count++;
        if (ImportGenres && scraped.Genres is { Count: > 0 }) count++;
        if (ImportCover && !string.IsNullOrWhiteSpace(MovieMetadataMapper.ResolveCoverUrl(scraped))) count++;
        return count;
    }
}
