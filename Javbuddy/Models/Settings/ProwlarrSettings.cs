using System.ComponentModel.DataAnnotations;
using Javbuddy.Services.Infrastructure;

namespace Javbuddy.Models;

/// <summary>Single-row settings for connecting to a Prowlarr instance.</summary>
public class ProwlarrSettings : IHasConnectionUrls
{
    public int Id { get; set; }

    [StringLength(500)]
    public string? BaseUrl { get; set; }

    [StringLength(500)]
    public string? ExternalUrl { get; set; }

    [StringLength(200)]
    public string? ApiKey { get; set; }
}
