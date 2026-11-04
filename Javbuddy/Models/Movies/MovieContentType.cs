namespace Javbuddy.Models;

/// <summary>What kind of release a movie is. Stored as the integer value, so new members go at the end.</summary>
public enum MovieContentType
{
    /// <summary>The default: a normal mosaic-censored release.</summary>
    Censored = 0,
    Uncensored = 1,
    /// <summary>A censored release whose mosaic was removed by a model.</summary>
    Decensored = 2,
}
