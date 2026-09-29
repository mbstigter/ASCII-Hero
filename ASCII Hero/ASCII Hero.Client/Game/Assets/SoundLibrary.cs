namespace ASCII_Hero.Client.Game.Assets;

/// <summary>
/// One synthesized sound effect, as read from a <c>SoundLibrary.ini</c> section: a single tone or
/// noise burst whose pitch slides from <see cref="StartFrequency"/> to <see cref="EndFrequency"/>
/// over <see cref="DurationSeconds"/>. Deliberately a single slide rather than a multi-note
/// sequence (see docs/Decisions.md).
/// </summary>
/// <param name="Name">The section name, used by game data to refer to this sound.</param>
/// <param name="Wave">Tone shape: Square, Triangle, Sawtooth, Sine or Noise (case-insensitive).</param>
/// <param name="StartFrequency">Pitch in Hz at the start of the sound (the filter center for Noise).</param>
/// <param name="EndFrequency">Pitch in Hz at the end of the sound; equals <see cref="StartFrequency"/> when not authored.</param>
/// <param name="DurationSeconds">Length of the sound in seconds.</param>
/// <param name="Volume">Loudness from 0 to 1, before the global master volume is applied.</param>
/// <param name="AttackSeconds">Fade-in time in seconds.</param>
/// <param name="VibratoHz">Optional pitch wobble rate in Hz; 0 means none.</param>
public readonly record struct SoundDefinition(
    string Name,
    string Wave,
    double StartFrequency,
    double EndFrequency,
    double DurationSeconds,
    double Volume,
    double AttackSeconds,
    double VibratoHz);

/// <summary>
/// The shared sound library (Global/SoundLibrary.ini merged with an optional world-local
/// SoundLibrary.ini: world entries override same-named sections, sections only defined globally
/// still apply). Loading/merging is handled by the shared <see cref="IniOverrideLoader"/>.
/// A name that isn't defined - or a missing file - simply means silence: there is no built-in
/// fallback sound.
/// </summary>
public class SoundLibrary
{
    /// <summary>A library with no sounds at all.</summary>
    public static SoundLibrary Empty { get; } = new(new Dictionary<string, SoundDefinition>());

    private readonly Dictionary<string, SoundDefinition> _sounds;

    private SoundLibrary(Dictionary<string, SoundDefinition> sounds) => _sounds = sounds;

    /// <summary>Every sound defined in this library.</summary>
    public IEnumerable<SoundDefinition> Definitions => _sounds.Values;

    public static async Task<SoundLibrary> LoadAsync(IAssetFileProvider fileProvider, string? worldName)
    {
        var sounds = await IniOverrideLoader.LoadAsync<string, SoundDefinition>(
            fileProvider, worldName, "SoundLibrary.ini", Merge, StringComparer.OrdinalIgnoreCase);
        return new SoundLibrary(sounds);
    }

    private static void Merge(Dictionary<string, SoundDefinition> sounds, IniDocument ini)
    {
        foreach (var sectionName in ini.SectionNames)
        {
            var section = ini.Section(sectionName);
            var startFrequency = IniValueParser.ParseDouble(section.GetValueOrDefault("StartFreq"));
            var endFrequency = section.TryGetValue("EndFreq", out var endFrequencyText) && IniValueParser.TryParseDouble(endFrequencyText, out var parsedEndFrequency)
                ? parsedEndFrequency
                : startFrequency;

            sounds[sectionName] = new SoundDefinition(
                sectionName,
                section.GetValueOrDefault("Wave") ?? string.Empty,
                startFrequency,
                endFrequency,
                IniValueParser.ParseDouble(section.GetValueOrDefault("Duration")),
                IniValueParser.ParseDouble(section.GetValueOrDefault("Volume")),
                IniValueParser.ParseDouble(section.GetValueOrDefault("Attack")),
                IniValueParser.ParseDouble(section.GetValueOrDefault("Vibrato")));
        }
    }
}
