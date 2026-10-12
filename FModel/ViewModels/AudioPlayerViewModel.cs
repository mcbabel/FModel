using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia.Threading;
using CUE4Parse.UE4.Criware.Decoders;
using CUE4Parse.UE4.Criware.Decoders.ADX;
using CUE4Parse.UE4.Criware.Decoders.HCA;
using CUE4Parse.Utils;
using FModel.Extensions;
using FModel.Framework;
using FModel.Framework.Audio;
using FModel.Services;
using FModel.Settings;
using FModel.ViewModels.Commands;
using FModel.Views.Resources.Controls;
using FModel.Views.Resources.Controls.Aup;
using FModel.Framework.Ui;
using Serilog;

namespace FModel.ViewModels;

public class AudioFile : ViewModel
{
    private string _filePath;
    public string FilePath
    {
        get => _filePath;
        private set => SetProperty(ref _filePath, value);
    }

    private string _fileName;
    public string FileName
    {
        get => _fileName;
        private set => SetProperty(ref _fileName, value);
    }

    private long _length;
    public long Length
    {
        get => _length;
        private set => SetProperty(ref _length, value);
    }

    private TimeSpan _duration = TimeSpan.Zero;
    public TimeSpan Duration
    {
        get => _duration;
        set => SetProperty(ref _duration, value);
    }

    private TimeSpan _position = TimeSpan.Zero;
    public TimeSpan Position
    {
        get => _position;
        set => SetProperty(ref _position, value);
    }

    private AudioEncoding _encoding = AudioEncoding.Unknown;
    public AudioEncoding Encoding
    {
        get => _encoding;
        set => SetProperty(ref _encoding, value);
    }

    private PlaybackState _playbackState = PlaybackState.Stopped;
    public PlaybackState PlaybackState
    {
        get => _playbackState;
        set => SetProperty(ref _playbackState, value);
    }

    private int _bytesPerSecond;
    public int BytesPerSecond
    {
        get => _bytesPerSecond;
        set => SetProperty(ref _bytesPerSecond, value);
    }

    public int Id { get; set; }
    public byte[] Data { get; set; }
    public string Extension { get; }

    public AudioFile(int id, byte[] data, string filePath)
    {
        Id = id;
        FilePath = filePath;
        FileName = filePath.SubstringAfterLast("/");
        Length = data.Length;
        Duration = TimeSpan.Zero;
        Position = TimeSpan.Zero;
        Encoding = AudioEncoding.Unknown;
        PlaybackState = PlaybackState.Stopped;
        BytesPerSecond = 0;
        Extension = filePath.SubstringAfterLast(".");
        Data = data;
    }

    public AudioFile(int id, string fileName)
    {
        Id = id;
        FilePath = string.Empty;
        FileName = fileName;
        Length = 0;
        Duration = TimeSpan.Zero;
        Position = TimeSpan.Zero;
        Encoding = AudioEncoding.Unknown;
        PlaybackState = PlaybackState.Stopped;
        BytesPerSecond = 0;
        Extension = string.Empty;
        Data = null;
    }

    public AudioFile(int id, FileInfo fileInfo)
    {
        Id = id;
        FilePath = fileInfo.FullName.Replace('\\', '/');
        FileName = fileInfo.Name;
        Length = fileInfo.Length;
        Duration = TimeSpan.Zero;
        Position = TimeSpan.Zero;
        Encoding = AudioEncoding.Unknown;
        PlaybackState = PlaybackState.Stopped;
        BytesPerSecond = 0;
        Extension = fileInfo.Extension[1..];
        Data = File.ReadAllBytes(fileInfo.FullName);
    }

    public AudioFile(AudioFile audioFile, IAudioDecoder wave)
    {
        Id = audioFile.Id;
        FilePath = audioFile.FilePath;
        FileName = audioFile.FileName;
        Length = audioFile.Length;
        Duration = wave.GetLength();
        Position = audioFile.Position;
        Encoding = wave.SourceFormat.Encoding;
        PlaybackState = audioFile.PlaybackState;
        BytesPerSecond = wave.SourceFormat.BytesPerSecond;
        Extension = audioFile.Extension;
        Data = audioFile.Data;
    }

    public override string ToString()
    {
        return $"{Id} | {FileName} | {Length}";
    }
}

public class AudioPlayerViewModel : ViewModel, ISource, IDisposable
{
    private DiscordHandler _discordHandler => DiscordService.DiscordHandler;
    private static IAudioDecoder _waveSource;
    private static AudioOutput _soundOut;
    private readonly DispatcherTimer _sourceTimer;
    private static bool _outputErrorReported;

    private TimeSpan _length => _waveSource?.GetLength() ?? TimeSpan.Zero;
    private TimeSpan _position => _soundOut?.PositionTime ?? _waveSource?.GetPosition() ?? TimeSpan.Zero;
    private PlaybackState _playbackState => _soundOut?.PlaybackState ?? PlaybackState.Stopped;
    private bool _hideToggle = false;

    public SpectrumProvider Spectrum { get; private set; }
    public float[] FftData { get; private set; }

    private AudioFile _playedFile = new(-1, "No audio file");
    public AudioFile PlayedFile
    {
        get => _playedFile;
        private set => SetProperty(ref _playedFile, value);
    }

    private AudioFile _selectedAudioFile;
    public AudioFile SelectedAudioFile
    {
        get => _selectedAudioFile;
        set => SetProperty(ref _selectedAudioFile, value);
    }

    private AudioDevice _selectedAudioDevice;
    public AudioDevice SelectedAudioDevice
    {
        get => _selectedAudioDevice;
        set => SetProperty(ref _selectedAudioDevice, value);
    }

    private AudioCommand _audioCommand;
    public AudioCommand AudioCommand => _audioCommand ??= new AudioCommand(this);

    public bool IsStopped => PlayedFile.PlaybackState == PlaybackState.Stopped;
    public bool IsPlaying => PlayedFile.PlaybackState == PlaybackState.Playing;
    public bool IsPaused => PlayedFile.PlaybackState == PlaybackState.Paused;

    private readonly ObservableCollection<AudioFile> _audioFiles;
    public ListCollectionView AudioFilesView { get; }
    public ListCollectionView AudioDevicesView { get; }

    public AudioPlayerViewModel()
    {
        _sourceTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(10) };
        _sourceTimer.Tick += TimerTick;
        _audioFiles = new ObservableCollection<AudioFile>();
        AudioFilesView = new ListCollectionView(_audioFiles);

        var audioDevices = new ObservableCollection<AudioDevice>(AudioDevice.Enumerate());
        AudioDevicesView = new ListCollectionView(audioDevices) { SortDescriptions = { new SortDescription("FriendlyName", ListSortDirection.Ascending) } };
        SelectedAudioDevice = audioDevices.FirstOrDefault(x => x.DeviceId == UserSettings.Default.AudioDeviceId)
                              ?? audioDevices.FirstOrDefault(x => x.IsDefault)
                              ?? audioDevices.FirstOrDefault();
    }

    public void Load()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            if (!ConvertIfNeeded())
                return;

            var decoder = CreateDecoder(SelectedAudioFile);
            if (decoder == null)
                return;

            ClearSoundOut();
            _waveSource?.Dispose();
            _waveSource = decoder;

            PlayedFile = new AudioFile(SelectedAudioFile, _waveSource);
            Spectrum = new SpectrumProvider(_waveSource.Channels, _waveSource.SampleRate, FftSize.Fft4096);
            FftData = new float[4096 + 4];

            RaiseSourceEvent(ESourceEventType.Loading);
            LoadSoundOut();
        });
    }

    public void Unload()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            ClearSoundOut();
            _waveSource?.Dispose();
            _waveSource = null;

            PlayedFile = new AudioFile(-1, "No audio file");
            Spectrum = null;

            RaiseSourceEvent(ESourceEventType.Clearing);
        });
    }

    public void AddToPlaylist(byte[] data, string filePath)
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            _audioFiles.Add(new AudioFile(_audioFiles.Count, data, filePath));
            if (_audioFiles.Count > 1) return;

            SelectedAudioFile = _audioFiles.Last();
            Load();
            Play();
        });
    }

    public void AddToPlaylist(string filePath)
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            _audioFiles.Add(new AudioFile(_audioFiles.Count, new FileInfo(filePath)));
            if (_audioFiles.Count > 1) return;

            SelectedAudioFile = _audioFiles.Last();
            Load();
            Play();
        });
    }

    public void Remove()
    {
        if (_audioFiles.Count < 1) return;
        Dispatcher.UIThread.Invoke(() =>
        {
            var removedPlaying = false;
            if (PlayedFile.Id == SelectedAudioFile.Id)
            {
                removedPlaying = true;
                Stop();
            }

            _audioFiles.RemoveAt(SelectedAudioFile.Id);
            for (var i = 0; i < _audioFiles.Count; i++)
            {
                _audioFiles[i].Id = i;
            }

            if (_audioFiles.Count < 1)
            {
                Unload();
                return;
            }

            SelectedAudioFile = SelectedAudioFile.Id >= _audioFiles.Count ? _audioFiles.Last() : _audioFiles[SelectedAudioFile.Id];

            if (!removedPlaying) return;
            Load();
            Play();
        });
    }

    public void Replace(AudioFile newAudio)
    {
        if (_audioFiles.Count < 1) return;
        Dispatcher.UIThread.Invoke(() =>
        {
            _audioFiles.Insert(SelectedAudioFile.Id, newAudio);
            _audioFiles.RemoveAt(SelectedAudioFile.Id + 1);
            SelectedAudioFile = newAudio;
        });
    }

    public void SavePlaylist()
    {
        if (_audioFiles.Count < 1) return;
        Dispatcher.UIThread.Invoke(() =>
        {
            foreach (var a in _audioFiles)
            {
                Save(a, true);
            }

            if (_audioFiles.Count > 1)
            {
                var dir = new DirectoryInfo(Path.GetDirectoryName(_audioFiles.First().FilePath));
                FLogger.Append(ELog.Information, () =>
                {
                    FLogger.Text($"Successfully saved {_audioFiles.Count} audio files to ", Constants.WHITE);
                    FLogger.Link(dir.Name, dir.FullName, true);
                });
            }
            else
            {
                FLogger.Append(ELog.Information, () =>
                {
                    FLogger.Text("Successfully saved ", Constants.WHITE);
                    FLogger.Link(_audioFiles.First().FileName, _audioFiles.First().FilePath, true);
                });
            }
        });
    }

    public void Save(AudioFile file = null, bool auto = false)
    {
        var fileToSave = file ?? SelectedAudioFile;
        if (_audioFiles.Count < 1 || fileToSave?.Data == null) return;
        var path = fileToSave.FilePath;

        if (!auto)
        {
            var saveFileDialog = new SaveFileDialog
            {
                Title = "Save Audio",
                FileName = fileToSave.FileName,
                InitialDirectory = UserSettings.Default.AudioDirectory
            };
            if (!saveFileDialog.ShowDialog().GetValueOrDefault()) return;
            path = saveFileDialog.FileName;
        }
        else
        {
            Directory.CreateDirectory(path.SubstringBeforeLast('/'));
        }

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        stream.Write(fileToSave.Data);

        if (File.Exists(path))
        {
            Log.Information("{FileName} successfully saved", fileToSave.FileName);
            if (!auto)
            {
                FLogger.Append(ELog.Information, () =>
                {
                    FLogger.Text("Successfully saved ", Constants.WHITE);
                    FLogger.Link(fileToSave.FileName, path, true);
                });
            }
        }
        else
        {
            Log.Error("{FileName} could not be saved", fileToSave.FileName);
            if (!auto)
            {
                FLogger.Append(ELog.Error, () => FLogger.Text($"Could not save '{fileToSave.FileName}'", Constants.WHITE, true));
            }
        }
    }

    public void PlayPauseOnStart()
    {
        if (IsStopped)
        {
            Load();
            Play();
        }
        else if (IsPaused)
        {
            Play();
        }
        else if (IsPlaying)
        {
            Pause();
        }
    }

    public void PlayPauseOnForce()
    {
        if (_audioFiles.Count < 1 || SelectedAudioFile.Id == PlayedFile.Id) return;

        Stop();
        Load();
        Play();
    }

    public void Next()
    {
        if (_audioFiles.Count < 1) return;

        Stop();
        SelectedAudioFile = _audioFiles.Next(PlayedFile.Id);
        Load();
        Play();
    }

    public void Previous()
    {
        if (_audioFiles.Count < 1) return;

        Stop();
        SelectedAudioFile = _audioFiles.Previous(PlayedFile.Id);
        Load();
        Play();
    }

    public void Play()
    {
        if (_soundOut == null || IsPlaying) return;
        _discordHandler.UpdateButDontSavePresence(null, $"Audio Player: {PlayedFile.FileName} ({PlayedFile.Duration:g})");
        _soundOut.Play();
    }

    public void Pause()
    {
        if (_soundOut == null || IsPaused) return;
        _soundOut.Pause();
    }

    public void Resume()
    {
        if (_soundOut == null || !IsPaused) return;
        _soundOut.Resume();
    }

    public void Stop()
    {
        if (_soundOut == null || IsStopped) return;
        _soundOut.Stop();
    }

    public void HideToggle()
    {
        if (!IsPlaying) return;
        _hideToggle = !_hideToggle;
        RaiseSourcePropertyChangedEvent(ESourceProperty.HideToggle, _hideToggle);
    }

    public void SkipTo(double percentage)
    {
        if (_soundOut == null || _waveSource == null) return;
        _soundOut.Position = (long) (_waveSource.Length * Math.Clamp(percentage, 0, 1));
    }

    public void Volume() => Volume(UserSettings.Default.AudioPlayerVolume);

    /// <param name="volume">0 to 100</param>
    public void Volume(float volume)
    {
        if (_soundOut == null) return;
        _soundOut.Volume = volume / 100;
    }

    public void Device()
    {
        if (_soundOut == null || _soundOut.Device?.DeviceId == SelectedAudioDevice?.DeviceId) return;

        var wasPlaying = _soundOut.PlaybackState == PlaybackState.Playing;
        _soundOut.Stop();
        LoadSoundOut();
        if (wasPlaying) _soundOut?.Play();
    }

    public void Dispose()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            _sourceTimer.Stop();
            ClearSoundOut();

            if (_waveSource != null)
            {
                _waveSource.Dispose();
                _waveSource = null;
            }

            if (Spectrum != null)
                Spectrum = null;

            foreach (var a in _audioFiles)
            {
                a.Data = null;
            }

            _audioFiles.Clear();
            PlayedFile = new AudioFile(-1, "No audio file");
        });
    }

    // runs on the UI thread (DispatcherTimer)
    private void TimerTick(object sender, EventArgs e)
    {
        if (_waveSource == null || _soundOut == null) return;

        var position = _position;
        if (position != PlayedFile.Position)
        {
            PlayedFile.Position = position;
            RaiseSourcePropertyChangedEvent(ESourceProperty.Position, PlayedFile.Position);
        }

        var playbackState = _playbackState;
        if (playbackState != PlayedFile.PlaybackState)
        {
            PlayedFile.PlaybackState = playbackState;
            RaiseSourcePropertyChangedEvent(ESourceProperty.PlaybackState, PlayedFile.PlaybackState);
        }

        if (Spectrum != null && FftData != null && PlayedFile.PlaybackState == PlaybackState.Playing)
        {
            Spectrum.GetFftData(FftData);
            RaiseSourcePropertyChangedEvent(ESourceProperty.FftData, FftData);
        }
    }

    private IAudioDecoder CreateDecoder(AudioFile file)
    {
        try
        {
            return AudioCodecFactory.GetCodec(file.Data, file.Extension);
        }
        catch (Exception e)
        {
            // WAVE files with an encoding we can't decode ourselves (ADPCM, XMA, ...) go through vgmstream
            if (TryDecodeWithVgmstream(file.Data, file.Extension, out var wavData))
            {
                try
                {
                    return AudioCodecFactory.GetCodec(wavData, "wav");
                }
                catch (Exception e2)
                {
                    e = e2;
                }
            }

            Log.Error(e, "Failed to decode {FileName}", file.FileName);
            FLogger.Append(ELog.Error, () => FLogger.Text($"Failed to decode '{file.FileName}': {e.Message}", Constants.WHITE, true));
            return null;
        }
    }

    private void LoadSoundOut()
    {
        if (_waveSource == null) return;
        ClearSoundOut();

        try
        {
            _soundOut = new AudioOutput(SelectedAudioDevice);
        }
        catch (Exception e)
        {
            Log.Error(e, "Failed to initialize the audio output");
            if (!_outputErrorReported)
            {
                _outputErrorReported = true;
                FLogger.Append(ELog.Error, () => FLogger.Text(OpenAlRuntime.IsAvailable
                    ? $"Could not open the audio output device: {e.Message}"
                    : "Audio playback is unavailable because OpenAL could not be loaded (install OpenAL Soft, e.g. libopenal1)", Constants.WHITE, true));
            }
            return;
        }

        var spectrum = Spectrum;
        _soundOut.SampleTap = (samples, count, _) => spectrum?.Add(samples, count);
        _soundOut.Initialize(_waveSource);
        _soundOut.Volume = UserSettings.Default.AudioPlayerVolume / 100;
        _sourceTimer.Start();
    }

    private void ClearSoundOut()
    {
        if (_soundOut == null) return;
        _soundOut.Dispose();
        _soundOut = null;
    }

    public event EventHandler<SourceEventArgs> SourceEvent;

    public event EventHandler<SourcePropertyChangedEventArgs> SourcePropertyChangedEvent = (sender, args) =>
    {
        if (sender is not AudioPlayerViewModel viewModel) return;
        switch (args.Property)
        {
            case ESourceProperty.PlaybackState:
            {
                if (_soundOut is { ReachedEnd: true } && (PlaybackState) args.Value == PlaybackState.Stopped)
                    viewModel.Next();

                break;
            }
        }
    };

    private void RaiseSourceEvent(ESourceEventType e)
    {
        SourceEvent?.Invoke(this, new SourceEventArgs(e));
    }

    private void RaiseSourcePropertyChangedEvent(ESourceProperty property, object value)
    {
        SourcePropertyChangedEvent?.Invoke(this, new SourcePropertyChangedEventArgs(property, value));
    }

    private bool ConvertIfNeeded()
    {
        if (SelectedAudioFile?.Data == null)
            return false;
        if (SelectedAudioFile.Extension == "wav")
            return true;

        if (!TryConvert(SelectedAudioFile.FilePath, SelectedAudioFile.Data, SelectedAudioFile.Extension, out var convertedFilePath, true))
            return false;

        var newAudio = new AudioFile(SelectedAudioFile.Id, new FileInfo(convertedFilePath));
        Replace(newAudio);

        return true;
    }

    public static bool TryConvert(string inputFilePath, byte[] inputFileData, string extension, out string wavFilePath, bool updateUi = false)
    {
        wavFilePath = string.Empty;

        switch (extension.ToLowerInvariant())
        {
            case "hca":
            case "adx":
                return TryConvertCriware(inputFilePath, inputFileData, extension, out wavFilePath);
            case "rada":
                return TryConvertRada(inputFilePath, inputFileData, extension, out wavFilePath, updateUi);
            default:
            {
                var vgmStreamPath = TryGetVgmstreamPath();
                if (string.IsNullOrEmpty(vgmStreamPath))
                    return false;

                var success = TryConvertToWav(inputFilePath, inputFileData, vgmStreamPath, true, out wavFilePath);

                if (!success)
                {
                    Log.Error("Failed to convert {InputFilePath} to .wav format", Path.GetFileName(inputFilePath));

                    if (updateUi)
                    {
                        FLogger.Append(ELog.Error, () =>
                        {
                            FLogger.Text("Failed to convert audio to .wav format. See: ", Constants.WHITE);
                            FLogger.Link("→ link ←", Constants.AUDIO_ISSUE_LINK, true);
                        });
                    }
                }

                return success;
            }
        }
    }

    private static bool TryConvertCriware(string inputFilePath, byte[] inputFileData, string extension, out string wavFilePath)
    {
        wavFilePath = string.Empty;

        try
        {
            byte[] wavData = extension switch
            {
                "hca" => HcaWaveStream.ConvertHcaToWav(inputFileData, UserSettings.Default.CurrentDir.CriwareDecryptionKey),
                "adx" => AdxDecoder.ConvertAdxToWav(inputFileData, UserSettings.Default.CurrentDir.CriwareDecryptionKey),
                _ => throw new NotSupportedException()
            };

            // Fallback for ADX
            if (wavData.Length == 0)
            {
                var vgmStreamPath = TryGetVgmstreamPath();
                if (string.IsNullOrEmpty(vgmStreamPath))
                    return false;

                return TryConvertToWav(inputFilePath, inputFileData, vgmStreamPath, true, out wavFilePath);
            }

            wavFilePath = Path.ChangeExtension(inputFilePath, ".wav");

            var directory = Path.GetDirectoryName(wavFilePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllBytes(wavFilePath, wavData);

            return true;
        }
        catch (CriwareDecryptionException ex)
        {
            FLogger.Append(ELog.Error, () => FLogger.Text($"Encrypted {extension.ToUpper()}: {ex.Message}", Constants.WHITE, true));
            Log.Error($"Encrypted {extension.ToUpper()}: {ex.Message}");

            return false;
        }
        catch (Exception ex)
        {
            FLogger.Append(ELog.Error, () => FLogger.Text($"Failed to convert {extension.ToUpper()}: {ex.Message}", Constants.WHITE, true));
            Log.Error($"Failed to convert {extension.ToUpper()}: {ex.Message}");

            return false;
        }
    }

    private static bool TryConvertRada(string inputFilePath, byte[] inputFileData, string extension, out string rawFilePath, bool updateUi = false)
    {
        rawFilePath = string.Empty;
        // radadec only exists for Windows, a native build can be dropped as ".data/radadec" on other platforms
        var decoderPath = Path.Combine(UserSettings.Default.OutputDirectory, ".data", OperatingSystem.IsWindows() ? $"{extension}dec.exe" : $"{extension}dec");
        if (!File.Exists(decoderPath))
        {
            Log.Error("Failed to convert {FilePath}, {Extension} decoder is missing", inputFilePath, extension);

            if (updateUi)
            {
                FLogger.Append(ELog.Error, () =>
                {
                    FLogger.Text($"Failed to convert audio because {extension} decoder is missing. See: ", Constants.WHITE);
                    FLogger.Link("→ link ←", Constants.RADA_ISSUE_LINK, true);
                });
            }

            return false;
        }

        return TryConvertToWav(inputFilePath, inputFileData, decoderPath, false, out rawFilePath);
    }

    private static bool TryConvertToWav(string inputFilePath, byte[] inputFileData, string converterPath, bool usevgmstream, out string wavFilePath)
    {
        wavFilePath = Path.ChangeExtension(inputFilePath, ".wav");
        var directory = Path.GetDirectoryName(inputFilePath);
        Directory.CreateDirectory(directory);

        var tempfile = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + Path.GetExtension(inputFilePath));
        File.WriteAllBytes(tempfile, inputFileData);

        var tempWavFilePath = Path.ChangeExtension(tempfile, ".wav");

        var success = RunConverter(converterPath, usevgmstream ? $"-o \"{tempWavFilePath}\" \"{tempfile}\"" : $"-i \"{tempfile}\" -o \"{tempWavFilePath}\"", 5000)
                      && File.Exists(tempWavFilePath);

        File.Delete(tempfile);

        if (success)
        {
            File.Move(tempWavFilePath, wavFilePath, true);
        }

        return success;
    }

    private static bool RunConverter(string converterPath, string arguments, int timeout)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = converterPath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (process == null) return false;
            if (!process.WaitForExit(timeout))
            {
                process.Kill(true);
                return false;
            }

            return process.ExitCode == 0;
        }
        catch (Exception e)
        {
            Log.Error(e, "Failed to run {Converter}", converterPath);
            return false;
        }
    }

    /// <summary>Decodes data we can't read ourselves (e.g. ADPCM .wav) to PCM .wav with vgmstream, without touching the exported file.</summary>
    private static bool TryDecodeWithVgmstream(byte[] data, string extension, out byte[] wavData)
    {
        wavData = null;
        if (data == null || data.Length == 0) return false;

        var vgmStreamPath = TryGetVgmstreamPath();
        if (string.IsNullOrEmpty(vgmStreamPath)) return false;

        var tempFile = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + "." + (string.IsNullOrEmpty(extension) ? "wav" : extension));
        var tempWav = Path.ChangeExtension(tempFile, ".decoded.wav");
        try
        {
            File.WriteAllBytes(tempFile, data);
            if (!RunConverter(vgmStreamPath, $"-o \"{tempWav}\" \"{tempFile}\"", 5000) || !File.Exists(tempWav))
                return false;

            wavData = File.ReadAllBytes(tempWav);
            return wavData.Length > 0;
        }
        catch (Exception e)
        {
            Log.Error(e, "vgmstream failed to decode audio");
            return false;
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
            if (File.Exists(tempWav)) File.Delete(tempWav);
        }
    }

    private static string TryGetVgmstreamPath()
    {
        var dataDir = Path.Combine(UserSettings.Default.OutputDirectory, ".data");
        string[] candidates = OperatingSystem.IsWindows() ? ["test.exe", "vgmstream-cli.exe"] : ["vgmstream-cli"];
        foreach (var candidate in candidates)
        {
            var vgmFilePath = Path.Combine(dataDir, candidate);
            if (File.Exists(vgmFilePath))
                return vgmFilePath;
        }

        Log.Error("Failed to convert audio, vgmstream is missing");
        FLogger.Append(ELog.Error, () =>
        {
            FLogger.Text("Failed to convert audio because vgmstream is missing. See: ", Constants.WHITE);
            FLogger.Link("→ link ←", Constants.AUDIO_ISSUE_LINK, true);
        });

        return string.Empty;
    }

    // Since Square Enix soundbanks are pretty niche, let's just use vgmstream to extract them
    public static List<string> ExtractSquareEnixAudio(string sabPath, byte[] sqexData)
    {
        var vgmStreamPath = TryGetVgmstreamPath();
        if (string.IsNullOrEmpty(vgmStreamPath))
            return [];
        if (sqexData.Length == 0)
            return [];

        var extractionDir = Path.GetDirectoryName(sabPath);
        Directory.CreateDirectory(extractionDir);

        // There's no clean way to know what was extracted with vgmstream (it's a soundbank, might contain multiple sounds) so we're monitoring extraction directory
        var capturedFiles = new ConcurrentBag<string>();
        using var watcher = new FileSystemWatcher(extractionDir)
        {
            Filter = "*.wav",
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime
        };

        void handler(object s, FileSystemEventArgs e) => capturedFiles.Add(e.FullPath);

        watcher.Created += handler;
        watcher.Changed += handler;
        watcher.EnableRaisingEvents = true;

        var tempSab = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".sab");
        File.WriteAllBytes(tempSab, sqexData);

        var startInfo = new ProcessStartInfo
        {
            FileName = vgmStreamPath,
            Arguments = $"-S 0 -o \"{Path.Combine(extractionDir, "?n_?s.wav")}\" \"{tempSab}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using (var process = Process.Start(startInfo))
        {
            process?.WaitForExit(15000);
        }

        File.Delete(tempSab);
        watcher.EnableRaisingEvents = false;

        return [.. capturedFiles.Distinct()];
    }
}
