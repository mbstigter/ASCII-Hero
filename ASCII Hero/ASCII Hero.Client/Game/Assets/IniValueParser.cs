using System.Globalization;

namespace ASCII_Hero.Client.Game.Assets;

/// <summary>
/// Shared parsing helpers for common ini value shapes used throughout settings.ini/*.ini asset
/// files: empty-char markers, single-character color codes, and culture-invariant numbers.
/// Naming follows the standard .NET <c>Parse</c>/<c>TryParse</c> convention: a <c>Parse...</c>
/// method always returns a value (falling back to a sensible default), while a
/// <c>TryParse...</c> method returns a bool and only assigns its out parameter on success.
/// </summary>
public static class IniValueParser
{
    /// <summary>
    /// Parses the "no cell here" marker character from a <c>[Layout] EmptyChar</c> settings.ini
    /// value, defaulting to a space when absent/empty.
    /// </summary>
    public static char ParseEmptyChar(string? rawValue) =>
        string.IsNullOrEmpty(rawValue) ? ' ' : rawValue[0];

    /// <summary>
    /// Parses a single-character color code (see <c>Global/ColorPalette.ini</c>) from a
    /// <c>DefaultForegroundColor</c>/<c>DefaultBackgroundColor</c>-shaped settings.ini value.
    /// Returns null if absent/empty; resolution against the palette happens at render time.
    /// </summary>
    public static char? ParseColorCode(string? rawValue) =>
        string.IsNullOrEmpty(rawValue) ? null : rawValue[0];

    /// <summary>
    /// Parses a numeric ini value with a 0.0 fallback when absent/unparsable, using
    /// <see cref="CultureInfo.InvariantCulture"/> so a decimal point (e.g. "1.0") is never
    /// misread as a thousands separator under locales where '.' is the group separator.
    /// </summary>
    public static double ParseDouble(string? rawValue) =>
        double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0.0;

    /// <summary>
    /// Attempts to parse a numeric ini value with <see cref="CultureInfo.InvariantCulture"/> so a
    /// decimal point (e.g. "1.0") is never misread as a thousands separator under locales where
    /// '.' is the group separator.
    /// </summary>
    public static bool TryParseDouble(string? rawValue, out double value) =>
        double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    /// <summary>Attempts to parse an integer ini value with <see cref="CultureInfo.InvariantCulture"/>.</summary>
    public static bool TryParseInt(string? rawValue, out int value) =>
        int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
}
