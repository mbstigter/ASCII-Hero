namespace ASCII_Hero.Client.Game.Assets;

/// <summary>
/// A minimal, hand-rolled parser for the simple ".ini"-style files used throughout the asset
/// format (settings.ini, Colors.ini, Materials.ini, *_objects.ini): "[Section]" headers,
/// "Key = Value" lines, ";" line comments (both full-line and trailing), and blank lines.
/// Not a general-purpose INI parser - only what AssetFormat.md actually specifies.
/// </summary>
public class IniDocument
{
    private readonly Dictionary<string, Dictionary<string, string>> _sections =
        new(StringComparer.OrdinalIgnoreCase);

    public static IniDocument Parse(string content)
    {
        var doc = new IniDocument();
        var currentSection = string.Empty;

        foreach (var rawLine in content.Replace("\r\n", "\n").Split('\n'))
        {
            var line = StripComment(rawLine).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                currentSection = line[1..^1].Trim();
                if (!doc._sections.ContainsKey(currentSection))
                {
                    doc._sections[currentSection] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }
                continue;
            }

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex < 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = Unquote(line[(separatorIndex + 1)..].Trim());

            if (!doc._sections.TryGetValue(currentSection, out var section))
            {
                section = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                doc._sections[currentSection] = section;
            }

            section[key] = value;
        }

        return doc;
    }

    /// <summary>Every key/value pair defined under the given section, or empty if the section is absent.</summary>
    public IReadOnlyDictionary<string, string> Section(string sectionName) =>
        _sections.TryGetValue(sectionName, out var section) ? section : new Dictionary<string, string>();

    /// <summary>
    /// Every section name defined in this document (e.g. every material name in a
    /// <c>Materials.ini</c>), in the order first encountered. Used by <see cref="MaterialLibrary"/>
    /// to enumerate all materials without needing to know their names up front.
    /// </summary>
    public IReadOnlyCollection<string> SectionNames => _sections.Keys;

    public string? TryGetValue(string sectionName, string key) =>
        _sections.TryGetValue(sectionName, out var section) && section.TryGetValue(key, out var value)
            ? value
            : null;

    private static string StripComment(string line)
    {
        // A ';' only starts a comment when not inside a quoted literal (e.g. EmptyChar = ';').
        var inQuote = false;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '\'')
            {
                inQuote = !inQuote;
            }
            else if (line[i] == ';' && !inQuote)
            {
                return line[..i];
            }
        }

        return line;
    }

    private static string Unquote(string value) =>
        value.Length >= 2 && value[0] == '\'' && value[^1] == '\'' ? value[1..^1] : value;
}
