using System;
using System.Buffers.Binary;
using System.Text;

namespace FModel.Framework.Audio;

/// <summary>
/// RIFF/WAVE decoder: PCM 8/16/24/32-bit, IEEE float 32/64-bit, A-law, µ-law and WAVE_FORMAT_EXTENSIBLE.
/// Multichannel files are downmixed to stereo.
/// </summary>
public sealed class WavDecoder : AudioDecoderBase
{
    private const ushort WAVE_FORMAT_PCM = 0x0001;
    private const ushort WAVE_FORMAT_IEEE_FLOAT = 0x0003;
    private const ushort WAVE_FORMAT_ALAW = 0x0006;
    private const ushort WAVE_FORMAT_MULAW = 0x0007;
    private const ushort WAVE_FORMAT_EXTENSIBLE = 0xFFFE;

    private enum ESampleType { Pcm, Float, ALaw, MuLaw }

    private readonly byte[] _data;
    private readonly int _dataOffset;
    private readonly int _dataLength;
    private readonly int _channels;
    private readonly int _blockAlign;
    private readonly int _bytesPerSample;
    private readonly ESampleType _sampleType;
    private readonly long _length;
    private long _position;

    public override AudioFormat SourceFormat { get; }
    public override int SampleRate { get; }
    public override long Length => _length;

    public override long Position
    {
        get => _position;
        set => _position = Math.Clamp(value, 0, _length);
    }

    /// <summary>Tag of the WAVE format (useful to report unsupported encodings).</summary>
    public ushort FormatTag { get; }

    public WavDecoder(byte[] data)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
        if (data.Length < 12 || !Matches(data, 0, "RIFF") || !Matches(data, 8, "WAVE"))
            throw new NotSupportedException("Not a RIFF/WAVE file");

        var fmtFound = false;
        var dataFound = false;
        ushort tag = 0, channels = 0, blockAlign = 0, bits = 0;
        uint sampleRate = 0, byteRate = 0, channelMask = 0;

        var pos = 12;
        while (pos + 8 <= data.Length)
        {
            var id = Encoding.ASCII.GetString(data, pos, 4);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos + 4));
            var body = pos + 8;
            var remaining = data.Length - body;

            switch (id)
            {
                case "fmt ":
                {
                    if (size < 16 || remaining < 16)
                        throw new NotSupportedException("Invalid WAVE fmt chunk");

                    var fmt = data.AsSpan(body);
                    tag = BinaryPrimitives.ReadUInt16LittleEndian(fmt);
                    channels = BinaryPrimitives.ReadUInt16LittleEndian(fmt[2..]);
                    sampleRate = BinaryPrimitives.ReadUInt32LittleEndian(fmt[4..]);
                    byteRate = BinaryPrimitives.ReadUInt32LittleEndian(fmt[8..]);
                    blockAlign = BinaryPrimitives.ReadUInt16LittleEndian(fmt[12..]);
                    bits = BinaryPrimitives.ReadUInt16LittleEndian(fmt[14..]);
                    FormatTag = tag;

                    if (tag == WAVE_FORMAT_EXTENSIBLE && size >= 40 && remaining >= 40)
                    {
                        // cbSize(2) validBits(2) channelMask(4) subFormat GUID(16): first 2 bytes are the actual format tag
                        channelMask = BinaryPrimitives.ReadUInt32LittleEndian(fmt[20..]);
                        var subFormat = BinaryPrimitives.ReadUInt16LittleEndian(fmt[24..]);
                        tag = subFormat;
                    }

                    fmtFound = true;
                    break;
                }
                case "data":
                {
                    _dataOffset = body;
                    // streamed/broken headers: size 0 or past the end of the file means "until the end"
                    _dataLength = size == 0 || size > remaining ? remaining : (int) size;
                    dataFound = true;
                    break;
                }
            }

            if (dataFound && fmtFound) break;
            if (size > int.MaxValue || body + (long) size > data.Length)
                break;

            pos = body + (int) size + (int) (size & 1); // chunks are word aligned
        }

        if (!fmtFound) throw new NotSupportedException("WAVE file has no fmt chunk");
        if (!dataFound) throw new NotSupportedException("WAVE file has no data chunk");
        if (channels == 0 || sampleRate == 0) throw new NotSupportedException("Invalid WAVE format");

        _channels = channels;
        if (blockAlign == 0) blockAlign = (ushort) (channels * ((bits + 7) / 8));
        _blockAlign = blockAlign;
        _bytesPerSample = blockAlign / channels;

        _sampleType = tag switch
        {
            WAVE_FORMAT_PCM when _bytesPerSample is >= 1 and <= 4 => ESampleType.Pcm,
            WAVE_FORMAT_IEEE_FLOAT when _bytesPerSample is 4 or 8 => ESampleType.Float,
            WAVE_FORMAT_ALAW when _bytesPerSample == 1 => ESampleType.ALaw,
            WAVE_FORMAT_MULAW when _bytesPerSample == 1 => ESampleType.MuLaw,
            _ => throw new NotSupportedException($"Unsupported WAVE encoding 0x{tag:X4} ({bits} bit)")
        };

        var encoding = FormatTag == WAVE_FORMAT_EXTENSIBLE
            ? AudioEncoding.Extensible
            : _sampleType switch
            {
                ESampleType.Pcm => AudioEncoding.Pcm,
                ESampleType.Float => AudioEncoding.IeeeFloat,
                ESampleType.ALaw => AudioEncoding.ALaw,
                ESampleType.MuLaw => AudioEncoding.MuLaw,
                _ => AudioEncoding.Unknown
            };

        SampleRate = (int) sampleRate;
        SourceFormat = new AudioFormat(encoding, (int) sampleRate, channels, bits, blockAlign, (int) byteRate);
        _length = _dataLength / _blockAlign;

        InitializeChannels(channels, ChannelMixer.WaveSpeakers(channels, (int) channelMask));
    }

    private static bool Matches(byte[] data, int offset, string magic)
    {
        for (var i = 0; i < magic.Length; i++)
            if (data[offset + i] != magic[i]) return false;
        return true;
    }

    protected override int ReadSource(float[] buffer, int offset, int count)
    {
        var frames = (int) Math.Min(count / _channels, _length - _position);
        if (frames <= 0) return 0;

        var samples = frames * _channels;
        var src = _dataOffset + (int) (_position * _blockAlign);
        var bps = _bytesPerSample;
        var span = _data.AsSpan();

        for (var f = 0; f < frames; f++)
        {
            var frameStart = src + f * _blockAlign;
            for (var c = 0; c < _channels; c++)
            {
                var p = frameStart + c * bps;
                buffer[offset++] = _sampleType switch
                {
                    ESampleType.Pcm => bps switch
                    {
                        1 => (_data[p] - 128) / 128f,
                        2 => BinaryPrimitives.ReadInt16LittleEndian(span[p..]) / 32768f,
                        3 => ((_data[p] << 8 | _data[p + 1] << 16 | _data[p + 2] << 24) >> 8) / 8388608f,
                        _ => BinaryPrimitives.ReadInt32LittleEndian(span[p..]) / 2147483648f
                    },
                    ESampleType.Float => bps == 4
                        ? BinaryPrimitives.ReadSingleLittleEndian(span[p..])
                        : (float) BinaryPrimitives.ReadDoubleLittleEndian(span[p..]),
                    ESampleType.ALaw => ALawToFloat(_data[p]),
                    ESampleType.MuLaw => MuLawToFloat(_data[p]),
                    _ => 0f
                };
            }
        }

        _position += frames;
        return samples;
    }

    private static float ALawToFloat(byte a)
    {
        a ^= 0x55;
        var sign = a & 0x80;
        var exponent = (a & 0x70) >> 4;
        var mantissa = a & 0x0F;
        var sample = exponent == 0 ? (mantissa << 4) + 8 : ((mantissa << 4) + 0x108) << (exponent - 1);
        return (sign != 0 ? sample : -sample) / 32768f;
    }

    private static float MuLawToFloat(byte u)
    {
        u = (byte) ~u;
        var sign = u & 0x80;
        var exponent = (u >> 4) & 0x07;
        var mantissa = u & 0x0F;
        var sample = (((mantissa << 3) + 0x84) << exponent) - 0x84;
        return (sign != 0 ? -sample : sample) / 32768f;
    }
}
