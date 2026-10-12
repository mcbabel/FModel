using System;

namespace FModel.Framework.Audio;

/// <summary>FFT sizes (replacement of CSCore.DSP.FftSize).</summary>
public enum FftSize
{
    Fft64 = 64,
    Fft128 = 128,
    Fft256 = 256,
    Fft512 = 512,
    Fft1024 = 1024,
    Fft2048 = 2048,
    Fft4096 = 4096,
    Fft8192 = 8192,
    Fft16384 = 16384
}

/// <summary>
/// Collects the most recent samples (channels averaged to mono) and computes their magnitude spectrum
/// with a Hann window and a radix-2 FFT (replacement of CSCore.DSP.FftProvider, same output scale: forward FFT normalized by N).
/// Thread safe: samples can be added from the audio thread while the UI reads the spectrum.
/// </summary>
public class FftProvider
{
    private readonly object _lock = new();
    private readonly float[] _storage;
    private readonly float[] _window;
    private readonly float[] _re;
    private readonly float[] _im;
    private readonly int _exponent;
    private int _storagePtr;

    public FftSize FftSize { get; }
    public int Channels { get; }
    public bool IsNewDataAvailable { get; private set; }

    public FftProvider(int channels, FftSize fftSize)
    {
        if (channels < 1)
            throw new ArgumentOutOfRangeException(nameof(channels));

        var size = (int) fftSize;
        if (size < 2 || (size & (size - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(fftSize));

        Channels = channels;
        FftSize = fftSize;
        _exponent = (int) Math.Log2(size);
        _storage = new float[size];
        _re = new float[size];
        _im = new float[size];
        _window = new float[size];
        for (var i = 0; i < size; i++)
            _window[i] = (float) (0.5 * (1 - Math.Cos(2 * Math.PI * i / (size - 1))));
    }

    public virtual void Add(float left, float right)
    {
        lock (_lock)
        {
            _storage[_storagePtr] = (left + right) / 2f;
            if (++_storagePtr >= _storage.Length) _storagePtr = 0;
            IsNewDataAvailable = true;
        }
    }

    /// <summary>Adds <paramref name="count"/> interleaved samples (<see cref="Channels"/> per frame).</summary>
    public virtual void Add(float[] samples, int count)
    {
        if (samples == null) throw new ArgumentNullException(nameof(samples));
        count = Math.Min(count, samples.Length);
        var channels = Channels;

        lock (_lock)
        {
            for (var i = 0; i + channels <= count; i += channels)
            {
                float sum = 0;
                for (var c = 0; c < channels; c++)
                    sum += samples[i + c];

                _storage[_storagePtr] = sum / channels;
                if (++_storagePtr >= _storage.Length) _storagePtr = 0;
            }

            if (count > 0) IsNewDataAvailable = true;
        }
    }

    /// <summary>Writes the magnitudes of the first N/2 bins into <paramref name="fftResultBuffer"/>.</summary>
    /// <returns>whether new data was added since the last call</returns>
    public virtual bool GetFftData(float[] fftResultBuffer)
    {
        if (fftResultBuffer == null) throw new ArgumentNullException(nameof(fftResultBuffer));

        bool result;
        var n = _storage.Length;
        lock (_lock)
        {
            // unroll the ring buffer (oldest sample first) and apply the window
            for (var i = 0; i < n; i++)
            {
                var s = _storage[(_storagePtr + i) % n];
                _re[i] = s * _window[i];
                _im[i] = 0;
            }

            result = IsNewDataAvailable;
            IsNewDataAvailable = false;
        }

        Fft(_re, _im, _exponent);

        var bins = Math.Min(n / 2, fftResultBuffer.Length);
        for (var i = 0; i < bins; i++)
            fftResultBuffer[i] = MathF.Sqrt(_re[i] * _re[i] + _im[i] * _im[i]);

        return result;
    }

    /// <summary>In-place iterative radix-2 forward FFT, normalized by N.</summary>
    public static void Fft(float[] re, float[] im, int exponent)
    {
        var n = 1 << exponent;

        // bit reversal
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
                j ^= bit;
            j ^= bit;

            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }

        for (var len = 2; len <= n; len <<= 1)
        {
            var angle = -2 * Math.PI / len;
            var wRe = (float) Math.Cos(angle);
            var wIm = (float) Math.Sin(angle);
            var half = len >> 1;
            for (var i = 0; i < n; i += len)
            {
                float curRe = 1, curIm = 0;
                for (var k = 0; k < half; k++)
                {
                    var a = i + k;
                    var b = a + half;
                    var tRe = re[b] * curRe - im[b] * curIm;
                    var tIm = re[b] * curIm + im[b] * curRe;
                    re[b] = re[a] - tRe;
                    im[b] = im[a] - tIm;
                    re[a] += tRe;
                    im[a] += tIm;

                    var nextRe = curRe * wRe - curIm * wIm;
                    curIm = curRe * wIm + curIm * wRe;
                    curRe = nextRe;
                }
            }
        }

        var scale = 1f / n;
        for (var i = 0; i < n; i++)
        {
            re[i] *= scale;
            im[i] *= scale;
        }
    }
}
