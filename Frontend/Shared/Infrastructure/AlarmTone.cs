using System.IO;
using System.Media;

namespace Frontend.Shared.Infrastructure;

/// <summary>
/// A short warning tone generated in memory (no sound file to ship, independent of the Windows sound scheme).
/// <see cref="Play"/> returns at once: SoundPlayer plays on its own thread, and a new Play replaces a tone still sounding.
/// </summary>
public sealed class AlarmTone
{
    private const int SampleRate = 22050;
    private readonly SoundPlayer player;

    public AlarmTone(double frequencyHz, TimeSpan length)
    {
        player = new SoundPlayer(new MemoryStream(Wave(frequencyHz, length)));
        player.Load();
    }

    public void Play() => player.Play();

    // 16-bit mono PCM: two beeps (each half of the length, the second half silent between them), with 5 ms fades so
    // the tone does not click.
    private static byte[] Wave(double frequencyHz, TimeSpan length)
    {
        var count = (int)(SampleRate * length.TotalSeconds);
        var beep = Math.Max(1, count / 4);
        var fade = SampleRate / 200;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write("RIFF"u8);
        writer.Write(36 + count * 2);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(SampleRate);
        writer.Write(SampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(count * 2);

        for (var i = 0; i < count; i++)
        {
            // Samples 0..beep and 2·beep..3·beep sound; the rest is silence.
            var position = i % (2 * beep);
            var sounding = position < beep && i < 4 * beep;
            var envelope = sounding ? Math.Min(1, Math.Min(position, beep - position) / (double)fade) : 0;
            writer.Write((short)(Math.Sin(2 * Math.PI * frequencyHz * i / SampleRate) * envelope * short.MaxValue * 0.6));
        }

        writer.Flush();
        return stream.ToArray();
    }
}
