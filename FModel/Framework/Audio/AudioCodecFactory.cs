using System;
using System.IO;

namespace FModel.Framework.Audio;

/// <summary>
/// Creates a decoder for in-memory audio data (replacement of the CSCore based CustomCodecFactory).
/// Supports RIFF/WAVE (PCM, IEEE float, A-law, µ-law, extensible), Ogg Vorbis and MPEG audio (mp1/mp2/mp3).
/// </summary>
public static class AudioCodecFactory
{
    /// <exception cref="NotSupportedException">the format (or the WAVE encoding) is not supported</exception>
    public static IAudioDecoder GetCodec(byte[] data, string extension)
    {
        if (data == null || data.Length == 0)
            throw new NotSupportedException("Audio data is empty");

        var ext = extension?.ToLowerInvariant() ?? string.Empty;

        // detect by content first, the extension can lie
        if (StartsWith(data, 0, "RIFF") && data.Length >= 12 && StartsWith(data, 8, "WAVE"))
            return new WavDecoder(data);
        if (StartsWith(data, 0, "OggS"))
            return new VorbisDecoder(new MemoryStream(data, false));
        if (StartsWith(data, 0, "ID3") || IsMpegFrameSync(data) || ext is "mp3" or "mpeg3" or "mp2" or "mp1")
            return new Mp3Decoder(new MemoryStream(data, false));

        throw new NotSupportedException($"Unsupported audio format '{extension}'");
    }

    private static bool StartsWith(byte[] data, int offset, string magic)
    {
        if (data.Length < offset + magic.Length) return false;
        for (var i = 0; i < magic.Length; i++)
            if (data[offset + i] != magic[i]) return false;
        return true;
    }

    private static bool IsMpegFrameSync(byte[] data)
        => data.Length >= 4 && data[0] == 0xFF && (data[1] & 0xE0) == 0xE0 && (data[1] & 0x06) != 0 && (data[2] & 0xF0) != 0xF0;
}
