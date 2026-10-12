using System;
using System.ComponentModel;

namespace FModel.Framework.Audio;

/// <summary>Playback state of an <see cref="AudioOutput"/> (replacement of CSCore.SoundOut.PlaybackState).</summary>
public enum PlaybackState
{
    Stopped,
    Playing,
    Paused
}

/// <summary>Encoding of the decoded source (replacement of CSCore.AudioEncoding), shown in the audio player.</summary>
public enum AudioEncoding
{
    [Description("Unknown")]
    Unknown = 0,
    [Description("Pcm")]
    Pcm,
    [Description("IeeeFloat")]
    IeeeFloat,
    [Description("ALaw")]
    ALaw,
    [Description("MuLaw")]
    MuLaw,
    [Description("Extensible")]
    Extensible,
    [Description("Vorbis")]
    Vorbis,
    [Description("MpegLayer3")]
    MpegLayer3
}

/// <summary>Format information of an audio source (what the stream contains, not what is sent to the device).</summary>
public sealed class AudioFormat
{
    public AudioEncoding Encoding { get; }
    public int SampleRate { get; }
    public int Channels { get; }
    public int BitsPerSample { get; }
    public int BlockAlign { get; }
    public int BytesPerSecond { get; }

    public AudioFormat(AudioEncoding encoding, int sampleRate, int channels, int bitsPerSample, int blockAlign = 0, int bytesPerSecond = 0)
    {
        Encoding = encoding;
        SampleRate = sampleRate;
        Channels = channels;
        BitsPerSample = bitsPerSample;
        BlockAlign = blockAlign > 0 ? blockAlign : Math.Max(1, channels * bitsPerSample / 8);
        BytesPerSecond = bytesPerSecond > 0 ? bytesPerSecond : BlockAlign * sampleRate;
    }

    public override string ToString() => $"{Encoding}, {SampleRate} Hz, {BitsPerSample} bit, {Channels} ch";
}

/// <summary>
/// A decoded, seekable audio stream producing interleaved 32-bit float samples.
/// Sources with more than two channels are downmixed to stereo, so <see cref="Channels"/> is 1 or 2.
/// </summary>
public interface IAudioDecoder : IDisposable
{
    /// <summary>Format of the original data (encoding, bit depth, channel count, bytes per second).</summary>
    AudioFormat SourceFormat { get; }

    int SampleRate { get; }

    /// <summary>Output channel count (1 or 2).</summary>
    int Channels { get; }

    /// <summary>Total length in sample frames.</summary>
    long Length { get; }

    /// <summary>Current position in sample frames.</summary>
    long Position { get; set; }

    /// <summary>Reads interleaved float samples. <paramref name="count"/> is a sample count (multiple of <see cref="Channels"/>).</summary>
    /// <returns>Number of samples read, 0 at the end of the stream.</returns>
    int Read(float[] buffer, int offset, int count);
}

public static class AudioDecoderExtensions
{
    public static TimeSpan GetLength(this IAudioDecoder decoder)
        => decoder == null || decoder.SampleRate <= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds((double) decoder.Length / decoder.SampleRate);

    public static TimeSpan GetPosition(this IAudioDecoder decoder)
        => decoder == null || decoder.SampleRate <= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds((double) decoder.Position / decoder.SampleRate);
}

/// <summary>Builds downmix matrices (N channels -> stereo).</summary>
internal static class ChannelMixer
{
    // WAVE_FORMAT_EXTENSIBLE speaker bits
    private const int FL = 0x1, FR = 0x2, FC = 0x4, LFE = 0x8, BL = 0x10, BR = 0x20, FLC = 0x40, FRC = 0x80,
        BC = 0x100, SL = 0x200, SR = 0x400, TC = 0x800, TFL = 0x1000, TFC = 0x2000, TFR = 0x4000, TBL = 0x8000,
        TBC = 0x10000, TBR = 0x20000;

    private const float Half = 0.7071f;

    /// <summary>Default speaker mask for a channel count (WAV channel order).</summary>
    public static int DefaultWaveMask(int channels) => channels switch
    {
        1 => FC,
        2 => FL | FR,
        3 => FL | FR | FC,
        4 => FL | FR | BL | BR,
        5 => FL | FR | FC | BL | BR,
        6 => FL | FR | FC | LFE | BL | BR,
        7 => FL | FR | FC | LFE | BC | SL | SR,
        8 => FL | FR | FC | LFE | BL | BR | SL | SR,
        _ => 0
    };

    /// <summary>Speaker positions in Vorbis channel order.</summary>
    public static int[] VorbisSpeakers(int channels) => channels switch
    {
        1 => [FC],
        2 => [FL, FR],
        3 => [FL, FC, FR],
        4 => [FL, FR, BL, BR],
        5 => [FL, FC, FR, BL, BR],
        6 => [FL, FC, FR, BL, BR, LFE],
        7 => [FL, FC, FR, SL, SR, BC, LFE],
        8 => [FL, FC, FR, SL, SR, BL, BR, LFE],
        _ => null
    };

    /// <summary>Speaker positions of each channel described by a WAVE channel mask (in channel order).</summary>
    public static int[] WaveSpeakers(int channels, int mask)
    {
        if (mask == 0) mask = DefaultWaveMask(channels);
        var speakers = new int[channels]; // 0 = unknown speaker
        var index = 0;
        for (var b = 0; b < 31 && index < channels; b++)
        {
            var bit = 1 << b;
            if ((mask & bit) != 0)
                speakers[index++] = bit;
        }
        return speakers;
    }

    /// <summary>Returns a [channels * 2] matrix (left, right coefficient per input channel).</summary>
    public static float[] CreateStereoMatrix(int[] speakers, int channels)
    {
        var matrix = new float[channels * 2];
        float sumL = 0, sumR = 0;
        for (var i = 0; i < channels; i++)
        {
            var speaker = speakers != null && i < speakers.Length ? speakers[i] : 0;
            var (l, r) = speaker switch
            {
                FL or FLC => (1f, 0f),
                FR or FRC => (0f, 1f),
                FC or TFC => (Half, Half),
                LFE => (0f, 0f),
                BL or SL or TFL or TBL => (Half, 0f),
                BR or SR or TFR or TBR => (0f, Half),
                BC or TC or TBC => (0.5f, 0.5f),
                _ => (0.5f, 0.5f)
            };
            matrix[i * 2] = l;
            matrix[i * 2 + 1] = r;
            sumL += l;
            sumR += r;
        }

        var scale = 1f / Math.Max(1f, Math.Max(sumL, sumR));
        for (var i = 0; i < matrix.Length; i++)
            matrix[i] *= scale;

        return matrix;
    }

    /// <summary>Downmixes <paramref name="frames"/> frames of <paramref name="src"/> (N channels) into stereo <paramref name="dst"/>.</summary>
    public static void Downmix(float[] src, int srcChannels, float[] matrix, float[] dst, int dstOffset, int frames)
    {
        for (var f = 0; f < frames; f++)
        {
            float l = 0, r = 0;
            var s = f * srcChannels;
            for (var c = 0; c < srcChannels; c++)
            {
                var v = src[s + c];
                l += v * matrix[c * 2];
                r += v * matrix[c * 2 + 1];
            }
            dst[dstOffset + f * 2] = l;
            dst[dstOffset + f * 2 + 1] = r;
        }
    }
}
