using System.ComponentModel.DataAnnotations;

namespace Javbuddy.Models;

/// <summary>The cup size an actor had from <see cref="EffectiveFrom"/> until the next period starts
///. Movie filters resolve an actress's cup by the movie's release date: the latest
/// period starting on or before it wins; before the first period, or without a release date,
/// <see cref="Actor.CupSize"/> applies.</summary>
public class ActorCupSizePeriod
{
    public int Id { get; set; }

    public int ActorId { get; set; }
    public Actor Actor { get; set; } = null!;

    public DateTime EffectiveFrom { get; set; }

    [Required]
    [StringLength(10)]
    public string CupSize { get; set; } = string.Empty;
}
