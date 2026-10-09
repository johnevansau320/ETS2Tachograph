namespace Tachograph.Core;

public sealed record Chime(byte[] Wav, int DurationMs);

/// <summary>Generates the short alert tones played before a spoken warning, as in-memory WAV data.</summary>
public static class ChimeSynth
{
    private const int SampleRate = 22050;

    // (frequency Hz, length ms) per tone; more urgent severities get more and higher tones.
    private static (int Hz, int Ms)[] Tones(Severity s) => s switch
    {
        Severity.Violation => new[] { (1175, 150), (880, 150), (1175, 150), (880, 150) },
        Severity.Warning => new[] { (880, 130), (1175, 130) },
        _ => new[] { (880, 120) }
    };

    public static Chime Create(Severity severity, double volume = 1.0)
    {
        volume = Math.Clamp(volume, 0, 1);
        double amp = 0.30 * volume * short.MaxValue;
        const int gapMs = 40, fadeMs = 8;

        var samples = new List<short>();
        foreach (var (hz, ms) in Tones(severity))
        {
            int n = SampleRate * ms / 1000, fade = SampleRate * fadeMs / 1000;
            for (int i = 0; i < n; i++)
            {
                double env = Math.Min(1.0, Math.Min(i, n - 1 - i) / (double)fade); // fade in and out, no clicks
                samples.Add((short)(Math.Sin(2 * Math.PI * hz * i / SampleRate) * amp * env));
            }
            samples.AddRange(new short[SampleRate * gapMs / 1000]);
        }

        int dataBytes = samples.Count * 2;
        using var ms2 = new MemoryStream(44 + dataBytes);
        using (var w = new BinaryWriter(ms2, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            w.Write("RIFF"u8); w.Write(36 + dataBytes); w.Write("WAVE"u8);
            w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(SampleRate); w.Write(SampleRate * 2); w.Write((short)2); w.Write((short)16);
            w.Write("data"u8); w.Write(dataBytes);
            foreach (var s in samples) w.Write(s);
        }
        return new Chime(ms2.ToArray(), samples.Count * 1000 / SampleRate);
    }
}
