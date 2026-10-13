namespace Javbuddy.Services.Infrastructure;

/// <summary>The parsing every integration's <c>*EnvConfig</c> repeated: typed reads of an
/// environment/appsettings key ("Section:Key", env form "Section__Key") that fall back to null/empty when
/// it is unset or malformed, so the setting's saved value applies instead.</summary>
public static class EnvConfig
{
    public static bool? GetBool(IConfiguration configuration, string key) =>
        bool.TryParse(configuration[key], out var value) ? value : null;

    /// <summary>Whether every given value is set (non-blank): an integration configured entirely from the
    /// environment needs all of its required fields.</summary>
    public static bool AllSet(params string?[] values) => values.All(v => !string.IsNullOrWhiteSpace(v));

    /// <summary>A list setting in any form .NET configuration offers: array entries (<c>Key__0</c>, JSON array
    /// in appsettings) or one string, which <paramref name="parseScalar"/> splits (JSON, comma or newline).</summary>
    public static List<string> GetList(IConfiguration configuration, string key, Func<string, List<string>> parseScalar)
    {
        var children = configuration.GetSection(key).GetChildren()
            .Select(c => c.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!.Trim())
            .ToList();
        if (children.Count > 0)
        {
            return children;
        }

        var scalar = configuration[key];
        return string.IsNullOrWhiteSpace(scalar) ? [] : parseScalar(scalar);
    }
}
