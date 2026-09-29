using ASCII_Hero.Client.Game.Assets;
using ASCII_Hero.Client.Game.Audio;
using Microsoft.JSInterop;

namespace ASCII_Hero.Client.Game.Browser;

/// <summary>
/// <see cref="ISoundPlayer"/> backed by the Web Audio API through the small, isolated
/// <c>audio.js</c> module, which synthesizes each sound from its <see cref="SoundDefinition"/>.
/// Together with <see cref="CanvasBridge"/> this is the only code that talks to JavaScript.
/// </summary>
public class WebAudioSoundPlayer(IJSRuntime jsRuntime) : ISoundPlayer, IAsyncDisposable
{
    private const string ModulePath = "./js/audio.js";

    private IJSObjectReference? _module;

    public async Task InitializeAsync(double masterVolume)
    {
        _module = await jsRuntime.InvokeAsync<IJSObjectReference>("import", ModulePath);
        await _module.InvokeVoidAsync("initialize", masterVolume);
    }

    public async Task SetSoundsAsync(SoundLibrary sounds)
    {
        if (_module is null)
        {
            return;
        }

        await _module.InvokeVoidAsync("setSounds", sounds.Definitions.ToArray());
    }

    public void Play(string soundName)
    {
        if (_module is null)
        {
            return;
        }

        _ = PlayAsync(soundName);
    }

    private async Task PlayAsync(string soundName)
    {
        try
        {
            await _module!.InvokeVoidAsync("play", soundName);
        }
        catch (JSException)
        {
            // A failed sound effect must never disturb the game loop.
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            await _module.InvokeVoidAsync("dispose");
            await _module.DisposeAsync();
        }
    }
}
