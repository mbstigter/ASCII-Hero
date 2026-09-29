using ASCII_Hero.Client.Game.Assets;

namespace ASCII_Hero.Client.Game.Audio;

/// <summary>
/// The game's only view of audio output: fire-and-forget playback of a named sound. Game logic
/// never touches audio APIs directly - it queues sound names on the world (see
/// <see cref="World.World2D.PlaySound"/>) and <see cref="GameLoop"/> forwards them here. Kept
/// behind an interface so the browser-specific implementation stays isolated in
/// <see cref="Browser.WebAudioSoundPlayer"/>.
/// </summary>
public interface ISoundPlayer
{
    /// <summary>Prepares audio output; <paramref name="masterVolume"/> is 0 (silent) to 1 (full).</summary>
    Task InitializeAsync(double masterVolume);

    /// <summary>Replaces the set of playable sounds, e.g. when a world finishes loading.</summary>
    Task SetSoundsAsync(SoundLibrary sounds);

    /// <summary>Plays the named sound once. Unknown names are ignored.</summary>
    void Play(string soundName);
}
