using System;
using System.IO;
using NVorbis;

namespace FModel.Framework.Audio;

/// <summary>Ogg Vorbis decoder (NVorbis).</summary>
public sealed class VorbisDecoder : AudioDecoderBase
{
    private readonly VorbisReader _vorbisReader;
    private bool _disposed;

    public override AudioFormat SourceFormat { get; }
    public override int SampleRate { get; }
    public override long Length => Math.Max(0, _vorbisReader.TotalSamples);

    public override long Position
    {
        get => _vorbisReader.SamplePosition;
        set => _vorbisReader.SamplePosition = Math.Clamp(value, 0, Length);
    }

    public VorbisDecoder(Stream stream)
    {
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));
        if (!stream.CanRead)
            throw new ArgumentException(@"Stream is not readable.", nameof(stream));

        _vorbisReader = new VorbisReader(stream, true);
        SampleRate = _vorbisReader.SampleRate;
        var bitrate = _vorbisReader.NominalBitrate > 0 ? _vorbisReader.NominalBitrate / 8 : 0;
        SourceFormat = new AudioFormat(AudioEncoding.Vorbis, _vorbisReader.SampleRate, _vorbisReader.Channels, 32,
            4 * _vorbisReader.Channels, bitrate);

        InitializeChannels(_vorbisReader.Channels, ChannelMixer.VorbisSpeakers(_vorbisReader.Channels));
    }

    protected override int ReadSource(float[] buffer, int offset, int count)
    {
        return _vorbisReader.ReadSamples(buffer, offset, count);
    }

    public override void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _vorbisReader.Dispose();
        base.Dispose();
    }
}
