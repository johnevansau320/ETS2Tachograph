using System.IO;
using System.Media;
using Tachograph.Core;

namespace Tachograph.App;

/// <summary>Plays the alert tone for a severity without blocking the UI.</summary>
public sealed class ChimePlayer : IDisposable
{
    private SoundPlayer? _player;
    private MemoryStream? _stream;

    /// <summary>Starts the chime and returns how long it lasts in milliseconds.</summary>
    public int Play(Severity severity, double volume)
    {
        var chime = ChimeSynth.Create(severity, volume);
        Stop();
        _stream = new MemoryStream(chime.Wav);
        _player = new SoundPlayer(_stream);
        _player.Play();
        return chime.DurationMs;
    }

    public void Stop()
    {
        _player?.Stop();
        _player?.Dispose();
        _stream?.Dispose();
        _player = null;
        _stream = null;
    }

    public void Dispose() => Stop();
}
