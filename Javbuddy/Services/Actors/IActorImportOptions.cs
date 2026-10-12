namespace Javbuddy.Services.Actors;

/// <summary>The per-field "import this" switches shared by every scraped actor source's import options.</summary>
public interface IActorImportOptions
{
    bool ImportJapaneseName { get; set; }
    bool ImportHeight { get; set; }
    bool ImportCupSize { get; set; }
    bool ImportMeasurements { get; set; }
    bool ImportBirthDate { get; set; }
    bool ImportRetiredStatus { get; set; }
    bool ImportAliases { get; set; }
}
