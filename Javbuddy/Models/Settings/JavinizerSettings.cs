using System.ComponentModel.DataAnnotations;
using Javbuddy.Services.Infrastructure;

namespace Javbuddy.Models;

/// <summary>Single-row settings for connecting to a javinizer-go instance.</summary>
public class JavinizerSettings : IHasConnectionUrls
{
    public int Id { get; set; }

    [StringLength(500)]
    public string? BaseUrl { get; set; }

    [StringLength(500)]
    public string? ExternalUrl { get; set; }

    [StringLength(500)]
    public string? ApiToken { get; set; }
}
