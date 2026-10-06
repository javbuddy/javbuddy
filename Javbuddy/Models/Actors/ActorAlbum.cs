using System.ComponentModel.DataAnnotations;

namespace Javbuddy.Models;

public class ActorAlbum
{
    public int Id { get; set; }

    public int ActorId { get; set; }

    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Actor? Actor { get; set; }

    public ICollection<ActorPhoto> Photos { get; } = new List<ActorPhoto>();
}
