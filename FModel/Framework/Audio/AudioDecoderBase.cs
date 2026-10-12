using System;

namespace FModel.Framework.Audio;

/// <summary>
/// Base class of the decoders: handles the downmix of multichannel sources to stereo.
/// Implementations read interleaved float samples in their own channel layout through <see cref="ReadSource"/>.
/// </summary>
public abstract class AudioDecoderBase : IAudioDecoder
{
    private float[] _mixMatrix;
    private float[] _scratch = [];
    private int _sourceChannels = 1;

    public abstract AudioFormat SourceFormat { get; }
    public abstract int SampleRate { get; }
    public int Channels { get; private set; } = 1;
    public abstract long Length { get; }
    public abstract long Position { get; set; }

    /// <param name="sourceChannels">channel count of the decoded data</param>
    /// <param name="speakers">speaker position of each channel (WAVE_FORMAT_EXTENSIBLE bits), used when downmixing</param>
    protected void InitializeChannels(int sourceChannels, int[] speakers)
    {
        if (sourceChannels < 1)
            throw new NotSupportedException($"Invalid channel count: {sourceChannels}");

        _sourceChannels = sourceChannels;
        if (sourceChannels <= 2)
        {
            Channels = sourceChannels;
            _mixMatrix = null;
        }
        else
        {
            Channels = 2;
            _mixMatrix = ChannelMixer.CreateStereoMatrix(speakers, sourceChannels);
        }
    }

    /// <summary>Reads <paramref name="count"/> interleaved samples in the source channel layout.</summary>
    protected abstract int ReadSource(float[] buffer, int offset, int count);

    public int Read(float[] buffer, int offset, int count)
    {
        if (_mixMatrix == null)
        {
            count -= count % Channels;
            return ReadSource(buffer, offset, count);
        }

        var frames = count / 2;
        var needed = frames * _sourceChannels;
        if (_scratch.Length < needed)
            _scratch = new float[needed];

        var read = 0;
        while (read < needed)
        {
            var r = ReadSource(_scratch, read, needed - read);
            if (r <= 0) break;
            read += r;
        }

        var framesRead = read / _sourceChannels;
        ChannelMixer.Downmix(_scratch, _sourceChannels, _mixMatrix, buffer, offset, framesRead);
        return framesRead * 2;
    }

    public virtual void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
