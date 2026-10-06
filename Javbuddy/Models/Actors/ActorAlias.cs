using System.ComponentModel.DataAnnotations;

namespace Javbuddy.Models;

/// <summary>Alternate stage name, spelling variation, or romanization linked to an actor.</summary>
public class ActorAlias
{
    public int Id { get; set; }

    public int ActorId { get; set; }
    public Actor Actor { get; set; } = null!;

    [Required]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
