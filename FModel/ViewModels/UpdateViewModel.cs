using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CUE4Parse.Utils;
using FModel.Framework;
using FModel.Services;
using FModel.Settings;
using FModel.ViewModels.ApiEndpoints.Models;
using FModel.ViewModels.Commands;
using Avalonia.Threading;

namespace FModel.ViewModels;

public partial class UpdateViewModel : ViewModel
{
    private ApiEndpointViewModel _apiEndpointView => ApplicationService.ApiEndpointView;

    private RemindMeCommand _remindMeCommand;
    public RemindMeCommand RemindMeCommand => _remindMeCommand ??= new RemindMeCommand(this);

    public RangeObservableCollection<GitHubCommit> Commits { get; }

    /// <summary>
    /// <see cref="Commits"/> grouped by (local) commit date, in order of first appearance
    /// (replacement of the WPF CollectionView PropertyGroupDescription on Commit.Author.Date).
    /// </summary>
    public ObservableCollection<CommitGroup> CommitGroups { get; } = [];

    private bool _hasCommits;
    public bool HasCommits
    {
        get => _hasCommits;
        private set => SetProperty(ref _hasCommits, value);
    }

    public UpdateViewModel()
    {
        Commits = [];
        Commits.CollectionChanged += OnCommitsChanged;

        if (UserSettings.Default.NextUpdateCheck < DateTime.Now)
            RemindMeCommand.Execute(this, null);
    }

    public async Task LoadAsync()
    {
        var commits = await _apiEndpointView.GitHubApi.GetCommitHistoryAsync();
        if (commits == null || commits.Length == 0)
            return;

        Commits.AddRange(commits);

        try
        {
            _ = LoadCoAuthors();
            _ = LoadAssets();
        }
        catch
        {
            //
        }
    }

    private Task LoadCoAuthors()
    {
        return Task.Run(async () =>
        {
            var coAuthorMap = new Dictionary<GitHubCommit, HashSet<string>>();
            foreach (var commit in Commits)
            {
                if (!commit.Commit.Message.Contains("Co-authored-by", StringComparison.OrdinalIgnoreCase))
                    continue;

                var regex = GetCoAuthorRegex();
                var matches = regex.Matches(commit.Commit.Message);
                if (matches.Count == 0) continue;

                commit.Commit.Message = regex.Replace(commit.Commit.Message, string.Empty).Trim();

                coAuthorMap[commit] = [];
                foreach (Match match in matches)
                {
                    if (match.Groups.Count < 3) continue;

                    var username = match.Groups[1].Value;
                    if (username.Equals("Asval", StringComparison.OrdinalIgnoreCase))
                    {
                        username = "4sval"; // found out the hard way co-authored usernames can't be trusted
                    } else if (username.Equals("Krowe Moh", StringComparison.OrdinalIgnoreCase))
                    {
                        username = "Krowe-moh";
                    }

                    coAuthorMap[commit].Add(username);
                }
            }

            if (coAuthorMap.Count == 0) return;

            var uniqueUsernames = coAuthorMap.Values.SelectMany(x => x).Distinct().ToArray();
            var authorCache = new Dictionary<string, Author>();
            foreach (var username in uniqueUsernames)
            {
                try
                {
                    var author = await _apiEndpointView.GitHubApi.GetUserAsync(username);
                    if (author != null)
                        authorCache[username] = author;
                }
                catch
                {
                    // Ignore
                }
            }

            foreach (var (commit, usernames) in coAuthorMap)
            {
                var coAuthors = usernames
                    .Where(authorCache.ContainsKey)
                    .Select(username => authorCache[username])
                    .ToArray();

                if (coAuthors.Length > 0)
                    Dispatcher.UIThread.Post(() => commit.CoAuthors = coAuthors);
            }
        });
    }

    private Task LoadAssets()
    {
        return Task.Run(async () =>
        {
            var qa = await _apiEndpointView.GitHubApi.GetReleaseAsync("qa");
            var assets = qa.Assets.OrderByDescending(x => x.CreatedAt).ToList();
            await Dispatcher.UIThread.InvokeAsync(() => ApplyAssets(assets));
        });
    }

    private void ApplyAssets(List<GitHubAsset> assets)
    {
        {
            for (var i = 0; i < assets.Count; i++)
            {
                var asset = assets[i];
                asset.IsLatest = i == 0;

                var commitSha = asset.Name.SubstringBeforeLast(".zip");
                var commit = Commits.FirstOrDefault(x => x.Sha == commitSha);
                if (commit != null)
                {
                    commit.Asset = asset;
                }
                else
                {
                    Commits.Add(new GitHubCommit
                    {
                        Sha = commitSha,
                        Commit = new Commit
                        {
                            Message = $"FModel ({commitSha[..7]})",
                            Author = new Author { Name = asset.Uploader.Login, Date = asset.CreatedAt }
                        },
                        Author = asset.Uploader,
                        Asset = asset
                    });
                }
            }
        }
    }

    private void OnCommitsChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(RebuildGroups);
            return;
        }

        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
        {
            foreach (GitHubCommit commit in e.NewItems)
                AddToGroup(commit);
        }
        else
        {
            RebuildGroups();
        }

        HasCommits = Commits.Count > 0;
    }

    private void RebuildGroups()
    {
        CommitGroups.Clear();
        foreach (var commit in Commits)
            AddToGroup(commit);

        HasCommits = Commits.Count > 0;
    }

    private void AddToGroup(GitHubCommit commit)
    {
        var date = DateOnly.FromDateTime((commit.Commit?.Author?.Date ?? DateTime.MinValue).ToLocalTime());
        var group = CommitGroups.FirstOrDefault(x => x.Date == date);
        if (group == null)
        {
            group = new CommitGroup(date);
            CommitGroups.Add(group);
        }

        group.Commits.Add(commit);
    }

    public void DownloadLatest()
    {
        Commits.FirstOrDefault(x => x.IsDownloadable && x.Asset.IsLatest)?.Download();
    }

    [GeneratedRegex(@"Co-authored-by:\s*(.+?)\s*<(.+?)>", RegexOptions.IgnoreCase | RegexOptions.Multiline, "en-US")]
    private static partial Regex GetCoAuthorRegex();
}

public class CommitGroup
{
    public DateOnly Date { get; }
    public string Header => string.Format(CultureInfo.InvariantCulture, "Commits on {0:MMM d, yyyy}", Date); // WPF HeaderStringFormat
    public ObservableCollection<GitHubCommit> Commits { get; } = [];

    public CommitGroup(DateOnly date)
    {
        Date = date;
    }
}
