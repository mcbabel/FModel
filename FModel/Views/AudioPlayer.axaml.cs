using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using FModel.Framework.Audio;
using FModel.Framework.Ui;
using FModel.Services;
using FModel.Settings;
using FModel.ViewModels;

namespace FModel.Views;

public partial class AudioPlayer : FWindow
{
    private ApplicationViewModel _applicationView => ApplicationService.ApplicationView;

    public AudioPlayer()
    {
        var area = ScreenInfo.PrimaryWorkingAreaDipsOf(this);
        Width = area.Width * 0.55;
        Height = area.Height * 0.50;

        DataContext = _applicationView;
        InitializeComponent();

        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        AudioFilesListBox.AddHandler(DoubleTappedEvent, OnAudioFileMouseDoubleClick, RoutingStrategies.Tunnel);
        Closing += OnClosing;
        Activated += OnActivatedDeactivated;
        Deactivated += OnActivatedDeactivated;
    }

    public void Load(byte[] data, string filePath)
    {
        _applicationView.AudioPlayer.AddToPlaylist(data, filePath);
    }

    private void OnClosing(object sender, WindowClosingEventArgs e)
    {
        _applicationView.AudioPlayer.Stop();
        _applicationView.AudioPlayer.Dispose();
        DiscordService.DiscordHandler.UpdateToSavedPresence();
    }

    private void OnDeviceSwap(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox { SelectedItem: AudioDevice selectedDevice })
            return;

        UserSettings.Default.AudioDeviceId = selectedDevice.DeviceId;
        _applicationView.AudioPlayer.Device();
    }

    private void OnVolumeChange(object sender, RangeBaseValueChangedEventArgs e)
    {
        _applicationView.AudioPlayer.Volume((float) e.NewValue);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Source is TextBox)
            return;

        if (UserSettings.Default.AddAudio.IsTriggered(e.Key))
        {
            e.Handled = true;
            var openFileDialog = new OpenFileDialog
            {
                Title = "Select an audio file",
                InitialDirectory = UserSettings.Default.AudioDirectory,
                Filter = "OGG Files (*.ogg)|*.ogg|WAV Files (*.wav)|*.wav|WEM Files (*.wem)|*.wem|ADPCM Files (*.adpcm)|*.adpcm|All Files (*.*)|*.*",
                Multiselect = true
            };

            if (!openFileDialog.ShowDialog().GetValueOrDefault()) return;
            foreach (var file in openFileDialog.FileNames)
            {
                _applicationView.AudioPlayer.AddToPlaylist(file);
            }
        }
        else if (UserSettings.Default.PlayPauseAudio.IsTriggered(e.Key))
            _applicationView.AudioPlayer.PlayPauseOnStart();
        else if (UserSettings.Default.PreviousAudio.IsTriggered(e.Key))
            _applicationView.AudioPlayer.Previous();
        else if (UserSettings.Default.NextAudio.IsTriggered(e.Key))
            _applicationView.AudioPlayer.Next();
        else if (UserSettings.Default.RemoveAudio.IsTriggered(e.Key))
            _applicationView.AudioPlayer.Remove();
        else
            return;

        e.Handled = true;
    }

    private void OnAudioFileMouseDoubleClick(object sender, TappedEventArgs e)
    {
        _applicationView.AudioPlayer.PlayPauseOnForce();
    }

    private void OnFilterTextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox textBox)
            return;

        var filters = (textBox.Text ?? string.Empty).Trim().Split(' ');
        _applicationView.AudioPlayer.AudioFilesView.Filter = o => o is AudioFile audio && filters.All(x => audio.FileName.Contains(x, StringComparison.OrdinalIgnoreCase));
    }

    private void OnActivatedDeactivated(object sender, EventArgs e)
    {
        _applicationView.AudioPlayer.HideToggle();
    }
}
