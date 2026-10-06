using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Javbuddy.Services.Nfo;

public static class NfoXmlFormatter
{
    /// <summary>Returns a pretty-printed (indented) serialization of <paramref name="doc"/>,
    /// suitable for human-readable diff preview. Unlike <see cref="NfoFileWriter.WriteXmlAsync"/>,
    /// this re-formats the document with 4-space indentation so the Monaco diff editor always shows
    /// each element on its own line, regardless of how the on-disk file was originally written.
    /// The XML declaration's encoding case (e.g. UTF-8) and standalone attribute are restored from
    /// <paramref name="doc"/>.<see cref="XDocument.Declaration"/>; a trailing newline is always
    /// appended so the saved file matches the convention used by most JAV scrapers.</summary>
    public static string SerializeXmlFormatted(XDocument doc)
    {
        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "    ",
            OmitXmlDeclaration = doc.Declaration is null,
            Encoding = new UTF8Encoding(false)
        };

        using var ms = new MemoryStream();
        using (var writer = XmlWriter.Create(ms, settings))
        {
            doc.Save(writer);
        }
        var result = Encoding.UTF8.GetString(ms.ToArray());

        // XmlWriter always writes encoding in lowercase and never includes standalone.
        // Restore both from the original declaration so the output matches the source file's style.
        if (doc.Declaration is { } decl)
        {
            if (!string.IsNullOrEmpty(decl.Encoding)
                && result.Contains("encoding=\"", StringComparison.OrdinalIgnoreCase))
            {
                // Replace the first occurrence only — encoding only appears in the declaration line.
                var lower = $"encoding=\"{decl.Encoding.ToLowerInvariant()}\"";
                var original = $"encoding=\"{decl.Encoding}\"";
                result = result.Replace(lower, original, StringComparison.OrdinalIgnoreCase);
            }

            if (!string.IsNullOrEmpty(decl.Standalone) && !result.Contains("standalone="))
            {
                result = result.Replace("?>", $" standalone=\"{decl.Standalone}\"?>");
            }
        }

        // Ensure a trailing newline — XmlWriter does not emit one.
        if (!result.EndsWith('\n'))
        {
            result += '\n';
        }

        return result;
    }
}
