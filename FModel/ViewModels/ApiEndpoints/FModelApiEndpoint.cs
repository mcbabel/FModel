using System;
using FModel.Framework.Ui;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CUE4Parse.Utils;
using FModel.Extensions;
using FModel.Framework;
using FModel.Services;
using FModel.Settings;
using FModel.ViewModels.ApiEndpoints.Models;
using FModel.Views;
using Newtonsoft.Json;
using RestSharp;
using Serilog;

namespace FModel.ViewModels.ApiEndpoints;

public class FModelApiEndpoint : AbstractApiProvider
{
    private News _news;
    private Info _infos;
    private Donator[] _donators;
    private Game _game;
    private readonly IDictionary<string, CommunityDesign> _communityDesigns = new Dictionary<string, CommunityDesign>();
    private ApplicationViewModel _applicationView => ApplicationService.ApplicationView;

    public FModelApiEndpoint(RestClient client) : base(client) { }

    public async Task<News> GetNewsAsync(CancellationToken token, string game)
    {
        var request = new FRestRequest($"https://api.fmodel.app/v1/news/{Constants.APP_VERSION}");
        request.AddParameter("game", game);
        var response = await _client.ExecuteAsync<News>(request, token).ConfigureAwait(false);
        Log.Information("[{Method}] [{Status}({StatusCode})] '{Resource}'", request.Method, response.StatusDescription, (int) response.StatusCode, response.ResponseUri?.OriginalString);
        return response.Data;
    }

    public News GetNews(CancellationToken token, string game)
    {
        return _news ??= GetNewsAsync(token, game).GetAwaiter().GetResult();
    }

    public async Task<Donator[]> GetDonatorsAsync()
    {
        var request = new FRestRequest($"https://api.fmodel.app/v1/donations/donators");
        var response = await _client.ExecuteAsync<Donator[]>(request).ConfigureAwait(false);
        Log.Information("[{Method}] [{Status}({StatusCode})] '{Resource}'", request.Method, response.StatusDescription, (int) response.StatusCode, response.ResponseUri?.OriginalString);
        return response.Data;
    }

    public Donator[] GetDonators()
    {
        return _donators ??= GetDonatorsAsync().GetAwaiter().GetResult();
    }

    public async Task<Game> GetGamesAsync(CancellationToken token, string gameName)
    {
        var request = new FRestRequest($"https://api.fmodel.app/v1/games/{gameName}");
        var response = await _client.ExecuteAsync<Game>(request, token).ConfigureAwait(false);
        Log.Information("[{Method}] [{Status}({StatusCode})] '{Resource}'", request.Method, response.StatusDescription, (int) response.StatusCode, response.ResponseUri?.OriginalString);
        return response.Data;
    }

    public Game GetGames(CancellationToken token, string gameName)
    {
        return _game ??= GetGamesAsync(token, gameName).GetAwaiter().GetResult();
    }

    public async Task<CommunityDesign> GetDesignAsync(string designName)
    {
        var request = new FRestRequest($"https://api.fmodel.app/v1/designs/{designName}");
        var response = await _client.ExecuteAsync<Community>(request).ConfigureAwait(false);
        Log.Information("[{Method}] [{Status}({StatusCode})] '{Resource}'", request.Method, response.StatusDescription, (int) response.StatusCode, response.ResponseUri?.OriginalString);
        return response.Data != null ? new CommunityDesign(response.Data) : null;
    }

    public CommunityDesign GetDesign(string designName)
    {
        if (_communityDesigns.TryGetValue(designName, out var communityDesign) && communityDesign != null)
            return communityDesign;

        communityDesign = GetDesignAsync(designName).GetAwaiter().GetResult();
        _communityDesigns[designName] = communityDesign;
        return communityDesign;
    }

    public void CheckForUpdates(bool launch = false)
    {
        if (DateTime.Now < UserSettings.Default.NextUpdateCheck)
        {
            Log.Warning("Updates have been silenced until {DateTime}", UserSettings.Default.NextUpdateCheck);
            return;
        }

        _ = Task.Run(async () =>
        {
            Info infos = null;
            try
            {
                var request = new FRestRequest("https://api.fmodel.app/v1/infos/Qa");
                var response = await _client.ExecuteAsync(request).ConfigureAwait(false);
                Log.Information("[{Method}] [{Status}({StatusCode})] '{Resource}'", request.Method, response.StatusDescription, (int) response.StatusCode, response.ResponseUri?.OriginalString);
                if (response.IsSuccessful && !string.IsNullOrEmpty(response.Content))
                    infos = JsonConvert.DeserializeObject<Info>(response.Content);
            }
            catch (Exception e)
            {
                Log.Error(e, "Update check failed");
            }

            Avalonia.Threading.Dispatcher.UIThread.Post(() => CheckForUpdateEvent(infos));
        });
    }

    private void CheckForUpdateEvent(Info infos)
    {
        _infos = infos;
        if (infos is { Version: not null })
        {
            UserSettings.Default.LastUpdateCheck = DateTime.Now;

            var currentVersionString = infos.Version.SubstringBefore('-');
            var targetHash = infos.Version.SubstringAfter('+');
            _applicationView.IsUpdateAvailable = targetHash != Constants.APP_COMMIT_ID;
            if (targetHash == Constants.APP_COMMIT_ID)
            {
                if (UserSettings.Default.ShowChangelog)
                    ShowChangelog(infos.ChangelogUrl, currentVersionString);

                return;
            }

            var currentVersion = new System.Version(currentVersionString);
            var installedVersion = System.Version.TryParse(Constants.APP_VERSION, out var v) ? v : new System.Version();
            UserSettings.Default.ShowChangelog = currentVersion != installedVersion;

            const string message = "A new update is available!";
            Log.Warning("{message} Version {CurrentVersion} ({Hash})", message, currentVersion, targetHash);
            Helper.OpenWindow<FWindow>(message, () => new UpdateView { Title = message, CanResize = false }.ShowDialog());
        }
        else
        {
            MessageBox.Show(
                "There is a problem reaching the update server, please check your internet connection or try again later.",
                "Update Check Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ShowChangelog(string changelogUrl, string version)
    {
        if (string.IsNullOrEmpty(changelogUrl)) return;

        var request = new FRestRequest(changelogUrl);
        var response = _client.Execute(request);
        if (!response.IsSuccessful || string.IsNullOrEmpty(response.Content)) return;

        _applicationView.CUE4Parse.TabControl.AddTab($"Release Notes: {version}");
        _applicationView.CUE4Parse.TabControl.SelectedTab.Highlighter = AvalonExtensions.HighlighterSelector("changelog");
        _applicationView.CUE4Parse.TabControl.SelectedTab.SetDocumentText(response.Content, false, false);
        UserSettings.Default.ShowChangelog = false;
    }
}
