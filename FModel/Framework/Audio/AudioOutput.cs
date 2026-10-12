using System;
using System.Collections.Generic;
using System.Threading;
using OpenTK.Audio.OpenAL;
using Serilog;

namespace FModel.Framework.Audio;

public class AudioOutputException(string message) : Exception(message);

/// <summary>
/// Streams an <see cref="IAudioDecoder"/> to an OpenAL device (replacement of CSCore's WasapiOut).
/// A background thread keeps a few small buffers queued; the playback position is exact (buffer start + AL sample offset).
/// </summary>
public sealed class AudioOutput : IDisposable
{
    private const int BufferCount = 6;
    private const int BufferFrames = 2048;

    private sealed class QueuedBuffer
    {
        public int Id;
        public long StartFrame;
        public int Frames;
        public float[] Samples;
        public bool Tapped;
    }

    private readonly ALDevice _device;
    private readonly ALContext _context;
    private readonly int _source;
    private readonly Dictionary<int, float[]> _bufferSamples = new();
    private readonly Stack<int> _freeBuffers = new();
    private readonly Queue<QueuedBuffer> _queued = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly bool _useFloat;
    private short[] _shortScratch = [];

    private IAudioDecoder _decoder;
    private ALFormat _format;
    private Thread _thread;
    private volatile bool _disposed;
    private volatile PlaybackState _playbackState = PlaybackState.Stopped;
    private bool _endOfStream;
    private float _volume = 1f;

    public AudioDevice Device { get; }
    public PlaybackState PlaybackState => _playbackState;

    /// <summary>True when playback stopped because the end of the source was reached.</summary>
    public bool ReachedEnd { get; private set; }

    /// <summary>Called on the audio thread with the interleaved samples (samples, count, channels) of the buffer that starts playing.</summary>
    public Action<float[], int, int> SampleTap { get; set; }

    public IAudioDecoder Source => _decoder;
    public long Length => _decoder?.Length ?? 0;

    public TimeSpan LengthTime => _decoder == null || _decoder.SampleRate <= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds((double) Length / _decoder.SampleRate);
    public TimeSpan PositionTime => _decoder == null || _decoder.SampleRate <= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds((double) Position / _decoder.SampleRate);

    /// <summary>Linear volume, 0 to 1.</summary>
    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0f, 1f);
            lock (OpenAlRuntime.Lock)
            {
                if (_disposed) return;
                MakeCurrent();
                AL.Source(_source, ALSourcef.Gain, _volume);
            }
        }
    }

    /// <summary>Playback position in sample frames. Setting it seeks.</summary>
    public long Position
    {
        get
        {
            lock (OpenAlRuntime.Lock)
                return GetPositionUnsafe();
        }
        set
        {
            lock (OpenAlRuntime.Lock)
            {
                if (_disposed || _decoder == null) return;
                MakeCurrent();
                var state = _playbackState;
                Flush();
                _decoder.Position = Math.Clamp(value, 0, Math.Max(0, _decoder.Length));
                ReachedEnd = false;

                if (state == PlaybackState.Stopped) return;
                Prefill();
                if (state == PlaybackState.Playing)
                    AL.SourcePlay(_source);
            }
            _wake.Set();
        }
    }

    /// <param name="device">device to open, null for the default one</param>
    /// <exception cref="AudioOutputException">OpenAL is missing or the device can't be opened</exception>
    public AudioOutput(AudioDevice device)
    {
        if (!OpenAlRuntime.IsAvailable)
            throw new AudioOutputException($"OpenAL is not available: {OpenAlRuntime.Error}");

        lock (OpenAlRuntime.Lock)
        {
            _device = ALC.OpenDevice(device?.DeviceId);
            if (_device == ALDevice.Null && device != null)
            {
                Log.Warning("Could not open audio device {Device}, falling back to the default one", device.DeviceId);
                _device = ALC.OpenDevice(null);
            }
            if (_device == ALDevice.Null)
                throw new AudioOutputException("Could not open an audio output device");

            _context = ALC.CreateContext(_device, (int[]) null);
            if (_context == ALContext.Null)
            {
                ALC.CloseDevice(_device);
                throw new AudioOutputException("Could not create an OpenAL context");
            }

            MakeCurrent();
            _source = AL.GenSource();
            AL.Source(_source, ALSourceb.SourceRelative, true);
            AL.Source(_source, ALSourceb.Looping, false);
            foreach (var buffer in AL.GenBuffers(BufferCount))
                _freeBuffers.Push(buffer);
            _useFloat = AL.IsExtensionPresent("AL_EXT_FLOAT32");
        }

        Device = device;
    }

    public void Initialize(IAudioDecoder decoder)
    {
        lock (OpenAlRuntime.Lock)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(AudioOutput));
            MakeCurrent();
            Flush();

            _decoder = decoder ?? throw new ArgumentNullException(nameof(decoder));
            _format = (decoder.Channels, _useFloat) switch
            {
                (1, true) => ALFormat.MonoFloat32Ext,
                (_, true) => ALFormat.StereoFloat32Ext,
                (1, false) => ALFormat.Mono16,
                _ => ALFormat.Stereo16
            };
            _bufferSamples.Clear();
            _playbackState = PlaybackState.Stopped;
            ReachedEnd = false;
        }

        if (_thread != null) return;
        _thread = new Thread(Run) { IsBackground = true, Name = "FModel Audio Output", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    public void Play()
    {
        lock (OpenAlRuntime.Lock)
        {
            if (_disposed || _decoder == null || _playbackState == PlaybackState.Playing) return;
            MakeCurrent();
            ReachedEnd = false;

            if (_playbackState == PlaybackState.Stopped)
            {
                Flush();
                Prefill();
                if (_queued.Count == 0) return; // nothing left to play
            }

            AL.SourcePlay(_source);
            _playbackState = PlaybackState.Playing;
        }
        _wake.Set();
    }

    public void Pause()
    {
        lock (OpenAlRuntime.Lock)
        {
            if (_disposed || _playbackState != PlaybackState.Playing) return;
            MakeCurrent();
            AL.SourcePause(_source);
            _playbackState = PlaybackState.Paused;
        }
    }

    public void Resume()
    {
        lock (OpenAlRuntime.Lock)
        {
            if (_disposed || _playbackState != PlaybackState.Paused) return;
            MakeCurrent();
            AL.SourcePlay(_source);
            _playbackState = PlaybackState.Playing;
        }
        _wake.Set();
    }

    /// <summary>Stops playback, the source stays at the position that was being played.</summary>
    public void Stop()
    {
        lock (OpenAlRuntime.Lock)
        {
            if (_disposed || _decoder == null) return;
            MakeCurrent();
            var position = GetPositionUnsafe();
            Flush();
            _decoder.Position = position;
            _playbackState = PlaybackState.Stopped;
            ReachedEnd = false;
        }
    }

    private void Run()
    {
        while (!_disposed)
        {
            try
            {
                lock (OpenAlRuntime.Lock)
                {
                    if (_disposed) break;
                    if (_playbackState == PlaybackState.Playing)
                        Service();
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Audio output error");
                _playbackState = PlaybackState.Stopped;
            }

            _wake.WaitOne(10);
        }
    }

    // called under lock while playing
    private void Service()
    {
        MakeCurrent();

        var processed = AL.GetSource(_source, ALGetSourcei.BuffersProcessed);
        if (processed > 0)
        {
            foreach (var id in AL.SourceUnqueueBuffers(_source, processed))
                _freeBuffers.Push(id);
            for (var i = 0; i < processed && _queued.Count > 0; i++)
                _queued.Dequeue();
        }

        while (!_endOfStream && _freeBuffers.Count > 0)
        {
            if (!QueueNextBuffer()) _endOfStream = true;
        }

        Tap();

        var state = (ALSourceState) AL.GetSource(_source, ALGetSourcei.SourceState);
        if (state == ALSourceState.Playing) return;

        if (_queued.Count > 0)
        {
            AL.SourcePlay(_source); // underrun, restart
        }
        else if (_endOfStream)
        {
            _playbackState = PlaybackState.Stopped;
            ReachedEnd = true;
        }
    }

    private void Prefill()
    {
        _endOfStream = false;
        while (_freeBuffers.Count > 0)
        {
            if (QueueNextBuffer()) continue;
            _endOfStream = true;
            break;
        }
        Tap();
    }

    private void Tap()
    {
        if (_queued.Count == 0) return;
        var head = _queued.Peek();
        if (head.Tapped) return;
        head.Tapped = true;

        try
        {
            SampleTap?.Invoke(head.Samples, head.Frames * _decoder.Channels, _decoder.Channels);
        }
        catch (Exception e)
        {
            Log.Error(e, "Audio sample tap failed");
        }
    }

    private bool QueueNextBuffer()
    {
        var channels = _decoder.Channels;
        var id = _freeBuffers.Peek();
        if (!_bufferSamples.TryGetValue(id, out var samples) || samples.Length != BufferFrames * channels)
            _bufferSamples[id] = samples = new float[BufferFrames * channels];

        var startFrame = _decoder.Position;
        var count = 0;
        while (count < samples.Length)
        {
            var read = _decoder.Read(samples, count, samples.Length - count);
            if (read <= 0) break;
            count += read;
        }

        var frames = count / channels;
        if (frames <= 0) return false;
        count = frames * channels;

        if (_useFloat)
        {
            AL.BufferData(id, _format, new ReadOnlySpan<float>(samples, 0, count), _decoder.SampleRate);
        }
        else
        {
            if (_shortScratch.Length < count) _shortScratch = new short[samples.Length];
            for (var i = 0; i < count; i++)
                _shortScratch[i] = (short) Math.Clamp(samples[i] * 32767f, short.MinValue, short.MaxValue);
            AL.BufferData(id, _format, new ReadOnlySpan<short>(_shortScratch, 0, count), _decoder.SampleRate);
        }

        AL.SourceQueueBuffers(_source, 1, ref id);
        _freeBuffers.Pop();
        _queued.Enqueue(new QueuedBuffer { Id = id, StartFrame = startFrame, Frames = frames, Samples = samples });
        return true;
    }

    // stops the source and gives back every buffer, called under lock
    private void Flush()
    {
        AL.SourceStop(_source);
        AL.Source(_source, ALSourcei.Buffer, 0);
        foreach (var queued in _queued)
            _freeBuffers.Push(queued.Id);
        _queued.Clear();
        _endOfStream = false;
    }

    private long GetPositionUnsafe()
    {
        if (_decoder == null) return 0;
        if (_disposed || _queued.Count == 0) return Math.Min(_decoder.Position, _decoder.Length);

        MakeCurrent();
        var head = _queued.Peek();
        var offset = AL.GetSource(_source, ALGetSourcei.SampleOffset);
        return Math.Clamp(head.StartFrame + offset, 0, _decoder.Length);
    }

    private void MakeCurrent() => ALC.MakeContextCurrent(_context);

    public void Dispose()
    {
        if (_disposed) return;
        lock (OpenAlRuntime.Lock)
        {
            if (_disposed) return;
            try
            {
                MakeCurrent();
                if (_decoder != null)
                {
                    var position = GetPositionUnsafe();
                    Flush();
                    _decoder.Position = position;
                }
                else
                {
                    AL.SourceStop(_source);
                }
            }
            catch (Exception e)
            {
                Log.Warning(e, "Failed to stop audio output");
            }
            _disposed = true;
            _playbackState = PlaybackState.Stopped;
        }

        _wake.Set();
        if (_thread != null && _thread != Thread.CurrentThread)
            _thread.Join(500);

        lock (OpenAlRuntime.Lock)
        {
            try
            {
                MakeCurrent();
                AL.DeleteSource(_source);
                AL.DeleteBuffers([.. _freeBuffers]);
                ALC.MakeContextCurrent(ALContext.Null);
                ALC.DestroyContext(_context);
                ALC.CloseDevice(_device);
            }
            catch (Exception e)
            {
                Log.Warning(e, "Failed to release audio output");
            }
        }

        _wake.Dispose();
    }
}
