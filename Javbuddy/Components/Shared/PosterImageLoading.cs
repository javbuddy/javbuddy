namespace Javbuddy.Components.Shared;

/// <summary>How a <see cref="PosterCard"/> should load its image.</summary>
public enum PosterImageLoading
{
    /// <summary>Plain <c>&lt;img src&gt;</c> — fetches immediately, and decodes atomically with
    /// the card. For grids where every rendered card is worth loading up front: either because the
    /// grid is small, or because it's windowed to a few viewports like Movies.razor's, where the
    /// point is precisely that a card's poster is fetched the moment the card exists rather than
    /// once the browser's own lazy-loading heuristic decides it's close enough.</summary>
    Eager,

    /// <summary>Browser-native <c>loading="lazy"</c> with a real <c>src</c> — for grids that
    /// render every result at once (ActorMissing), where loading the whole list up front would
    /// be wasteful and the browser's distance heuristic is the right call.</summary>
    NativeLazy,

    /// <summary><c>data-src</c> with no <c>src</c>, picked up by an IntersectionObserver-based
    /// JS module — what ActorDetail.razor's movie grid uses instead of <see cref="NativeLazy"/>.
    /// Native <c>loading="lazy"</c> only defers correctly on the initial static parse; once
    /// Blazor Server's interactive circuit connects, the whole grid is re-inserted as fresh DOM
    /// nodes in one batch, and Chromium's distance heuristic fails open (loads everything at
    /// once) for nodes it can't yet measure at insertion time — confirmed live via a Playwright
    /// network trace against a 284-movie actor page.</summary>
    JsLazy,
}
