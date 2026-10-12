using System;
using System.IO;
using NLayer;

namespace FModel.Framework.Audio;

/// <summary>MPEG layer I/II/III decoder (NLayer).</summary>
public sealed class Mp3Decoder : AudioDecoderBase
{
    private readonly MpegFile _mpegFile;
    private readonly int _sourceChannels;
    private bool _disposed;

    public override AudioFormat SourceFormat { get; }
    public override int SampleRate { get; }

    // NLayer positions are expressed in bytes of decoded 32-bit float samples
    private long BytesPerFrame => _sourceChannels * sizeof(float);
    public override long Length => Math.Max(0, _mpegFile.Length / BytesPerFrame);

    public override long Position
    {
        get => _mpegFile.Position / BytesPerFrame;
        set => _mpegFile.Position = Math.Clamp(value, 0, Length) * BytesPerFrame;
    }

    public Mp3Decoder(Stream stream)
    {
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));

        _mpegFile = new MpegFile(stream);
        _sourceChannels = _mpegFile.Channels;
        SampleRate = _mpegFile.SampleRate;
        if (_sourceChannels < 1 || SampleRate <= 0)
            throw new NotSupportedException("Invalid MPEG audio stream");

        var duration = _mpegFile.Duration.TotalSeconds;
        var bytesPerSecond = duration > 0 ? (int) (stream.Length / duration) : 0;
        SourceFormat = new AudioFormat(AudioEncoding.MpegLayer3, SampleRate, _sourceChannels, 16, 0, bytesPerSecond);

        InitializeChannels(_sourceChannels, ChannelMixer.WaveSpeakers(_sourceChannels, 0));
    }

    protected override int ReadSource(float[] buffer, int offset, int count)
    {
        return _mpegFile.ReadSamples(buffer, offset, count);
    }

    public override void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _mpegFile.Dispose();
        base.Dispose();
    }
}
