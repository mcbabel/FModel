using FModel.Framework;
using Newtonsoft.Json;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using CUE4Parse.UE4.Objects.Core.Serialization;
using CUE4Parse.UE4.Versions;
using CUE4Parse.Utils;
using FModel.Settings;
using FModel.ViewModels.ApiEndpoints.Models;

namespace FModel.ViewModels;

public class GameSelectorViewModel : ViewModel
{
    public class DetectedGame
    {
        public string GameName { get; set; }
        public string GameDirectory { get; set; }
        public EGame OverridedGame { get; set; }
        public bool IsManual { get; set; }

        // the followings are only used when game is manually added
        public AesResponse AesKeys { get; set; }
        public List<FCustomVersion> OverridedCustomVersions { get; set; }
        public Dictionary<string, bool> OverridedOptions { get; set; }
        public Dictionary<string, KeyValuePair<string, string>> OverridedMapStructTypes { get; set; }
        public IList<CustomDirectory> CustomDirectories { get; set; }
    }

    private DirectorySettings _selectedDirectory;
    public DirectorySettings SelectedDirectory
    {
        get => _selectedDirectory;
        set => SetProperty(ref _selectedDirectory, value);
    }

    private readonly ObservableCollection<DirectorySettings> _detectedDirectories;
    public ReadOnlyObservableCollection<DirectorySettings> DetectedDirectories { get; }
    public ReadOnlyObservableCollection<EGame> UeGames { get; }

    // case-insensitive file/directory lookups so that games installed through Proton/Wine (".EXE", "Paks", ...) are found on Linux too
    private static readonly EnumerationOptions _caseInsensitiveTopLevel = new()
    {
        MatchCasing = MatchCasing.CaseInsensitive,
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        AttributesToSkip = 0
    };
    private static readonly EnumerationOptions _caseInsensitiveRecursive = new()
    {
        MatchCasing = MatchCasing.CaseInsensitive,
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        // on Unix, don't follow symlinked directories (wine prefixes contain links to "/" which would make the search endless)
        AttributesToSkip = OperatingSystem.IsWindows() ? 0 : FileAttributes.ReparsePoint
    };

    public GameSelectorViewModel(string gameDirectory)
    {
        _detectedDirectories = new ObservableCollection<DirectorySettings>(EnumerateDetectedGames().Where(x => x != null));
        foreach (var dir in UserSettings.Default.PerDirectory.Values.Where(x => x.IsManual))
        {
            _detectedDirectories.Add((DirectorySettings) dir.Clone());
        }

        DetectedDirectories = new ReadOnlyObservableCollection<DirectorySettings>(_detectedDirectories);

        if (DetectedDirectories.FirstOrDefault(x => x.GameDirectory == gameDirectory) is { } detectedGame)
            SelectedDirectory = detectedGame;
        else if (IsGameDirectoryAvailable(gameDirectory))
            AddUndetectedDir(gameDirectory);
        else
            SelectedDirectory = DetectedDirectories.FirstOrDefault();

        UeGames = new ReadOnlyObservableCollection<EGame>(new ObservableCollection<EGame>(EnumerateUeGames()));
    }

    public void AddUndetectedDir(string gameDirectory) => AddUndetectedDir(GetLastPathSegment(gameDirectory), gameDirectory);
    public void AddUndetectedDir(string gameName, string gameDirectory)
    {
        gameDirectory = Helper.ExpandPath(gameDirectory);
        var ueVersion = EGame.GAME_UE4_LATEST;
        try
        {
            TryDetectUeVersion(gameDirectory, out ueVersion, out _);
        }
        catch (Exception e)
        {
            Serilog.Log.Warning(e, "Could not detect the Unreal Engine version of {Directory}", gameDirectory);
        }

        var setting = DirectorySettings.Default(gameName, gameDirectory, true, ueVersion);
        UserSettings.Default.PerDirectory[gameDirectory] = setting;
        _detectedDirectories.Add(setting);
        SelectedDirectory = DetectedDirectories.Last();
    }

    /// <summary>
    /// Last segment of a path, accepting both '\' and '/' separators (Windows behavior was SubstringAfterLast('\\')).
    /// </summary>
    private static string GetLastPathSegment(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        var trimmed = path.TrimEnd('\\', '/');
        if (trimmed.Length == 0) return path;
        return trimmed[(trimmed.LastIndexOfAny(['\\', '/']) + 1)..];
    }

    /// <summary>
    /// Appends a Windows style relative path ("\\FortniteGame\\Content\\Paks") to an install location.
    /// Windows keeps the exact same string as before (settings are keyed by game directory), other platforms use their separator.
    /// </summary>
    private static string JoinGamePath(string root, string relative)
    {
        if (OperatingSystem.IsWindows())
            return $"{root}{relative}";

        return Path.Combine(root ?? string.Empty, relative.Replace('\\', '/').TrimStart('/'));
    }

    private bool TryDetectUeVersion(string gameDirectory, out EGame ueVersion, [MaybeNullWhen(false)] out string newGameDirectory)
    {
        var targetGameDir = gameDirectory;
        if (!targetGameDir.EndsWith("Paks", StringComparison.OrdinalIgnoreCase))
        {
            var crashReportClientPaks = Path.Combine("Engine", "Programs", "CrashReportClient", "Content", "Paks");
            var dirs = Directory.GetDirectories(targetGameDir, "Paks", _caseInsensitiveRecursive);
            var paksDir = dirs.Length == 1 ? dirs[0] : dirs.FirstOrDefault(x => !x.EndsWith(crashReportClientPaks, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(paksDir))
            {
                Log.Warning("Selected directory \"{GameDirectory}\" does not end with \"Paks\". Looking in \"{PaksDir}\" instead.", targetGameDir, paksDir);
                targetGameDir = paksDir;
            }

            if (Directory.GetFiles(gameDirectory, "*.exe", _caseInsensitiveTopLevel) is { Length: 1 } exe && TryGetUeVersionFromExe(exe[0], out ueVersion))
            {
                // we checked the exe in the original directory, the BootstrapPackagedGame one
                // but we still want c4p to use the paks folder as the game directory (if any), not the original one
                newGameDirectory = targetGameDir;
                Log.Information("Detected UE version {UeVersion} from \"{Exe}\"", ueVersion, exe[0]);
                return true;
            }
        }

        // past this point, we assume targetGameDir is the correct Paks folder
        newGameDirectory = targetGameDir;
        var projectDir = Path.Combine(targetGameDir, "..", "..");

        var projectBinariesDir = Path.Combine(projectDir, "Binaries", "Win64");
        if (Directory.Exists(projectBinariesDir))
        {
            if (Directory.GetFiles(projectBinariesDir, "*-Win64-Shipping.exe", _caseInsensitiveTopLevel) is { Length: > 0 } shipping)
            {
                foreach (var exe in shipping)
                {
                    if (TryGetUeVersionFromExe(exe, out ueVersion))
                    {
                        Log.Information("Detected UE version {UeVersion} from \"{Exe}\"", ueVersion, exe);
                        return true;
                    }
                }
            }
            else if (Directory.GetFiles(projectBinariesDir, "*.exe", _caseInsensitiveTopLevel) is { Length: < 3 } exes)
            {
                foreach (var exe in exes)
                {
                    if (TryGetUeVersionFromExe(exe, out ueVersion))
                    {
                        Log.Information("Detected UE version {UeVersion} from \"{Exe}\"", ueVersion, exe);
                        return true;
                    }
                }
            }
        }

        var projectEngineBinariesDir = Path.Combine(projectDir, "..", "Engine", "Binaries", "Win64");

        if (Directory.Exists(projectEngineBinariesDir))
        {
            var crashReportClientExe = Directory.GetFiles(projectEngineBinariesDir, "CrashReportClient.exe", _caseInsensitiveTopLevel).FirstOrDefault()
                                       ?? Path.Combine(projectEngineBinariesDir, "CrashReportClient.exe");
            if (File.Exists(crashReportClientExe) && TryGetUeVersionFromExe(crashReportClientExe, out ueVersion))
            {
                Log.Information("Detected UE version {UeVersion} from \"{Exe}\"", ueVersion, crashReportClientExe);
                return true;
            }
            if (Directory.GetFiles(projectEngineBinariesDir, "*-Win64-Shipping.exe", _caseInsensitiveTopLevel) is { Length: > 0 } shipping)
            {
                foreach (var exe in shipping)
                {
                    if (TryGetUeVersionFromExe(exe, out ueVersion))
                    {
                        Log.Information("Detected UE version {UeVersion} from \"{Exe}\"", ueVersion, exe);
                        return true;
                    }
                }
            }
        }

        ueVersion = EGame.GAME_UE4_LATEST;
        Log.Warning("Failed to detect UE version for \"{GameDirectory}\".", gameDirectory);
        return false;
    }

    private bool TryGetUeVersionFromExe(string exePath, out EGame ueVersion)
    {
        ueVersion = EGame.GAME_UE4_LATEST;
        try
        {
            int fileMajorPart, fileMinorPart;
            if (OperatingSystem.IsWindows())
            {
                var info = FileVersionInfo.GetVersionInfo(exePath);
                fileMajorPart = info.FileMajorPart;
                fileMinorPart = info.FileMinorPart;
            }
            else if (!TryReadPeFileVersion(exePath, out fileMajorPart, out fileMinorPart))
            {
                // FileVersionInfo does not read native PE version resources outside of Windows
                return false;
            }

            ueVersion = fileMajorPart switch
            {
                4 => (EGame) Math.Min((uint)(GameUtils.GameUe4Base + (fileMinorPart << 16)), (uint) EGame.GAME_UE4_LATEST),
                5 => (EGame) Math.Min((uint)(GameUtils.GameUe5Base + (fileMinorPart << 16)), (uint) EGame.GAME_UE5_LATEST),
                _ => throw new InvalidOperationException($"Unsupported UE major version {fileMajorPart} detected from {exePath}")
            };
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Cross-platform reader of the VS_FIXEDFILEINFO (dwFileVersionMS) of a Windows PE file's version resource.
    /// </summary>
    private static bool TryReadPeFileVersion(string path, out int major, out int minor)
    {
        const uint VS_FFI_SIGNATURE = 0xFEEF04BD;
        major = minor = 0;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var headers = new PEHeaders(fs);
            if (headers.PEHeader is not { } peHeader) return false;

            var resourceDir = peHeader.ResourceTableDirectory;
            if (resourceDir.RelativeVirtualAddress == 0 || resourceDir.Size == 0) return false;

            foreach (var section in headers.SectionHeaders)
            {
                var sectionSize = Math.Max(section.VirtualSize, section.SizeOfRawData);
                if (resourceDir.RelativeVirtualAddress < section.VirtualAddress ||
                    resourceDir.RelativeVirtualAddress >= section.VirtualAddress + sectionSize)
                    continue;

                var delta = resourceDir.RelativeVirtualAddress - section.VirtualAddress;
                var length = Math.Min(resourceDir.Size, section.SizeOfRawData - delta);
                if (length <= 0) return false;

                var buffer = new byte[length];
                fs.Seek(section.PointerToRawData + delta, SeekOrigin.Begin);
                fs.ReadExactly(buffer, 0, buffer.Length);

                for (var i = 0; i + 16 <= buffer.Length; i += 4) // VS_FIXEDFILEINFO is DWORD aligned
                {
                    if (BitConverter.ToUInt32(buffer, i) != VS_FFI_SIGNATURE) continue;

                    var fileVersionMs = BitConverter.ToUInt32(buffer, i + 8);
                    major = (int) (fileVersionMs >> 16);
                    minor = (int) (fileVersionMs & 0xFFFF);
                    return true;
                }

                return false;
            }
        }
        catch
        {
            // not a PE file or unreadable
        }

        return false;
    }

    public void DeleteSelectedGame()
    {
        UserSettings.Default.PerDirectory.Remove(SelectedDirectory.GameDirectory); // should not be a problem
        _detectedDirectories.Remove(SelectedDirectory);
        SelectedDirectory = DetectedDirectories.Last();
    }

    public int ClearMissingDirectories()
    {
        var removedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var missingDirectories = UserSettings.Default.PerDirectory
            .Where(x => !IsGameDirectoryAvailable(x.Value.GameDirectory))
            .Select(x => new KeyValuePair<string, string>(x.Key, x.Value.GameDirectory))
            .ToArray();

        foreach (var (key, gameDirectory) in missingDirectories)
        {
            UserSettings.Default.PerDirectory.Remove(key);
            removedDirectories.Add(gameDirectory);
        }

        var missingDetectedDirectories = _detectedDirectories
            .Where(x => !IsGameDirectoryAvailable(x.GameDirectory))
            .ToArray();

        foreach (var directory in missingDetectedDirectories)
        {
            _detectedDirectories.Remove(directory);
            removedDirectories.Add(directory.GameDirectory);
        }

        if (SelectedDirectory is null || !_detectedDirectories.Contains(SelectedDirectory))
            SelectedDirectory = DetectedDirectories.FirstOrDefault();

        return removedDirectories.Count;
    }

    public static bool IsGameDirectoryAvailable(string gameDirectory)
        => gameDirectory is Constants._FN_LIVE_TRIGGER or Constants._VAL_LIVE_TRIGGER || Directory.Exists(gameDirectory);

    private IEnumerable<EGame> EnumerateUeGames()
        => Enum.GetValues<EGame>()
            .GroupBy(value => (int)value)
            .Select(group => group.First())
            .OrderBy(value => ((int)value & 0xFF) == 0);
    private IEnumerable<DirectorySettings> EnumerateDetectedGames()
    {
        yield return GetUnrealEngineGame("Fortnite", "\\FortniteGame\\Content\\Paks", EGame.GAME_UE6_0);
        yield return DirectorySettings.Default("Fortnite [LIVE]", Constants._FN_LIVE_TRIGGER, ue: EGame.GAME_UE6_0);
        yield return GetUnrealEngineGame("Pewee", "\\RogueCompany\\Content\\Paks", EGame.GAME_RogueCompany);
        yield return GetUnrealEngineGame("Rosemallow", "\\Indiana\\Content\\Paks", EGame.GAME_UE4_21);
        yield return GetUnrealEngineGame("Catnip", "\\OakGame\\Content\\Paks", EGame.GAME_Borderlands3);
        yield return GetUnrealEngineGame("AzaleaAlpha", "\\Prospect\\Content\\Paks", EGame.GAME_UE4_27);
        yield return GetUnrealEngineGame("shoebill", "\\SwGame\\Content\\Paks", EGame.GAME_StarWarsJediFallenOrder);
        yield return GetUnrealEngineGame("Snoek", "\\StateOfDecay2\\Content\\Paks", EGame.GAME_StateOfDecay2);
        yield return GetUnrealEngineGame("711c5e95dc094ca58e5f16bd48e751d6", "\\MultiVersus\\Content\\Paks", EGame.GAME_UE4_26);
        yield return GetUnrealEngineGame("9361c8c6d2f34b42b5f2f61093eedf48", "\\TslGame\\Content\\Paks", EGame.GAME_PlayerUnknownsBattlegrounds);
        yield return GetRiotGame("VALORANT", "ShooterGame\\Content\\Paks", EGame.GAME_Valorant);
        yield return DirectorySettings.Default("VALORANT [LIVE]", Constants._VAL_LIVE_TRIGGER, ue: EGame.GAME_Valorant);
        yield return GetSteamGame(381210, "\\DeadByDaylight\\Content\\Paks", EGame.GAME_DeadByDaylight, aesKey: "0x22b1639b548124925cf7b9cbaa09f9ac295fcf0324586d6b37ee1d42670b39b3"); // Dead By Daylight
        yield return GetSteamGame(578080, "\\TslGame\\Content\\Paks", EGame.GAME_PlayerUnknownsBattlegrounds); // PUBG
        yield return GetSteamGame(1172380, "\\SwGame\\Content\\Paks", EGame.GAME_StarWarsJediFallenOrder); // STAR WARS Jedi: Fallen Order™
        yield return GetSteamGame(677620, "\\PortalWars\\Content\\Paks", EGame.GAME_Splitgate); // Splitgate
        yield return GetSteamGame(1172620, "\\Athena\\Content\\Paks", EGame.GAME_SeaOfThieves); // Sea of Thieves
        yield return GetSteamGame(1665460, "\\pak", EGame.GAME_UE4_26); // eFootball 2023
        yield return GetRockstarGamesGame("GTA III - Definitive Edition", "\\Gameface\\Content\\Paks", EGame.GAME_GTATheTrilogyDefinitiveEdition);
        yield return GetRockstarGamesGame("GTA San Andreas - Definitive Edition", "\\Gameface\\Content\\Paks", EGame.GAME_GTATheTrilogyDefinitiveEdition);
        yield return GetRockstarGamesGame("GTA Vice City - Definitive Edition", "\\Gameface\\Content\\Paks", EGame.GAME_GTATheTrilogyDefinitiveEdition);
        yield return GetLevelInfiniteGame("tof_launcher", "\\Hotta\\Content\\Paks", EGame.GAME_TowerOfFantasy);
    }

    private List<Installation> _epicInstallations;
    private DirectorySettings GetUnrealEngineGame(string gameName, string pakDirectory, EGame ueVersion)
    {
        _epicInstallations ??= EnumerateEpicInstallations().ToList();
        foreach (var installation in _epicInstallations)
        {
            if (installation.AppName == null || installation.InstallLocation == null)
                continue;

            var gameDir = JoinGamePath(installation.InstallLocation, pakDirectory);
            if (installation.AppName.Equals(gameName, StringComparison.OrdinalIgnoreCase) && Directory.Exists(gameDir))
            {
                Log.Debug("Found {GameName} in {Source}", gameName, installation.Source);
                return DirectorySettings.Default(installation.AppName, gameDir, ue: ueVersion);
            }
        }

        return null;
    }

    /// <summary>
    /// Epic Games installations: Epic Games Launcher (Windows), and on Linux/macOS Heroic Games Launcher / legendary
    /// (native and flatpak) as well as the Epic Games Launcher running inside common Wine prefixes.
    /// </summary>
    private IEnumerable<Installation> EnumerateEpicInstallations()
    {
        if (OperatingSystem.IsWindows())
        {
            var launcherInstalled = GetDriveLauncherInstalls<LauncherInstalled>("ProgramData\\Epic\\UnrealEngineLauncher\\LauncherInstalled.dat");
            if (launcherInstalled?.InstallationList == null)
                yield break;

            foreach (var installation in launcherInstalled.InstallationList)
            {
                installation.Source = "LauncherInstalled.dat";
                yield return installation;
            }

            yield break;
        }

        foreach (var installedJson in EnumerateLegendaryInstalledFiles())
        {
            Dictionary<string, LegendaryInstalledGame> games = null;
            try
            {
                games = JsonConvert.DeserializeObject<Dictionary<string, LegendaryInstalledGame>>(File.ReadAllText(installedJson));
            }
            catch (Exception e)
            {
                Log.Warning(e, "Failed to read \"{InstalledJson}\"", installedJson);
            }

            if (games == null) continue;

            Log.Debug("\"{InstalledJson}\" found", installedJson);
            foreach (var (key, game) in games)
            {
                if (game == null) continue;
                yield return new Installation
                {
                    AppName = game.AppName ?? key,
                    InstallLocation = game.InstallPath,
                    AppVersion = game.Version,
                    Source = installedJson
                };
            }
        }

        foreach (var prefix in EnumerateWinePrefixes())
        {
            var launcherInstalledDat = Path.Combine(prefix, "drive_c", "ProgramData", "Epic", "UnrealEngineLauncher", "LauncherInstalled.dat");
            if (!File.Exists(launcherInstalledDat)) continue;

            LauncherInstalled launcherInstalled = null;
            try
            {
                launcherInstalled = JsonConvert.DeserializeObject<LauncherInstalled>(File.ReadAllText(launcherInstalledDat));
            }
            catch (Exception e)
            {
                Log.Warning(e, "Failed to read \"{LauncherInstalled}\"", launcherInstalledDat);
            }

            if (launcherInstalled?.InstallationList == null) continue;

            Log.Debug("\"{LauncherInstalled}\" found in wine prefix \"{Prefix}\"", launcherInstalledDat, prefix);
            foreach (var installation in launcherInstalled.InstallationList)
            {
                var unixPath = WinePathToUnix(prefix, installation.InstallLocation);
                if (unixPath == null) continue;

                installation.InstallLocation = unixPath;
                installation.Source = launcherInstalledDat;
                yield return installation;
            }
        }
    }

    private static string GetHomeDirectory() => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static string GetXdgConfigHome()
    {
        var xdgConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        return !string.IsNullOrEmpty(xdgConfigHome) ? xdgConfigHome : Path.Combine(GetHomeDirectory(), ".config");
    }

    private static IEnumerable<string> EnumerateLegendaryInstalledFiles()
    {
        var home = GetHomeDirectory();
        var configHome = GetXdgConfigHome();
        var candidates = new List<string>
        {
            // Heroic Games Launcher (native / AppImage / distro package)
            Path.Combine(configHome, "heroic", "legendaryConfig", "legendary", "installed.json"),
            // Heroic Games Launcher (flatpak)
            Path.Combine(home, ".var", "app", "com.heroicgameslauncher.hgl", "config", "heroic", "legendaryConfig", "legendary", "installed.json"),
            // standalone legendary
            Path.Combine(configHome, "legendary", "installed.json")
        };

        var legendaryConfigPath = Environment.GetEnvironmentVariable("LEGENDARY_CONFIG_PATH");
        if (!string.IsNullOrEmpty(legendaryConfigPath))
            candidates.Insert(0, Path.Combine(legendaryConfigPath, "installed.json"));

        return candidates.Distinct().Where(File.Exists);
    }

    private static IEnumerable<string> EnumerateWinePrefixes()
    {
        var home = GetHomeDirectory();
        var prefixes = new List<string>();

        var winePrefix = Environment.GetEnvironmentVariable("WINEPREFIX");
        if (!string.IsNullOrEmpty(winePrefix))
            prefixes.Add(winePrefix);

        prefixes.Add(Path.Combine(home, ".wine"));
        prefixes.Add(Path.Combine(home, "Games", "epic-games-store")); // Lutris default for the Epic Games Store

        return prefixes.Distinct().Where(p => Directory.Exists(Path.Combine(p, "drive_c")));
    }

    /// <summary>"C:\Program Files\Epic Games\Fortnite" -> "&lt;prefix&gt;/drive_c/Program Files/Epic Games/Fortnite"</summary>
    private static string WinePathToUnix(string prefix, string windowsPath)
    {
        if (string.IsNullOrEmpty(windowsPath) || windowsPath.Length < 2 || windowsPath[1] != ':')
            return null;

        var drive = char.ToLowerInvariant(windowsPath[0]);
        var root = Path.Combine(prefix, "dosdevices", $"{drive}:");
        if (!Directory.Exists(root))
            root = drive == 'c' ? Path.Combine(prefix, "drive_c") : null;
        if (root == null)
            return null;

        var rest = windowsPath[2..].Replace('\\', '/').Trim('/');
        return rest.Length == 0 ? root : Path.Combine(root, rest);
    }

    private RiotClientInstalls _riotClientInstalls;
    private DirectorySettings GetRiotGame(string gameName, string pakDirectory, EGame ueVersion)
    {
        if (!OperatingSystem.IsWindows()) // Riot games don't run on Linux (Vanguard)
            return null;

        _riotClientInstalls ??= GetDriveLauncherInstalls<RiotClientInstalls>("ProgramData\\Riot Games\\RiotClientInstalls.json");
        if (_riotClientInstalls is { AssociatedClient: { } })
        {
            foreach (var (key, _) in _riotClientInstalls.AssociatedClient)
            {
                var gameDir = $"{key.Replace('/', '\\')}{pakDirectory}";
                if (key.Contains(gameName, StringComparison.OrdinalIgnoreCase) && Directory.Exists(gameDir))
                {
                    Log.Debug("Found {GameName} in RiotClientInstalls.json", gameName);
                    return DirectorySettings.Default(gameName, gameDir, ue: ueVersion);
                }
            }
        }

        return null;
    }

    private DirectorySettings GetSteamGame(int id, string pakDirectory, EGame ueVersion, string aesKey = "")
    {
        var steamInfo = SteamDetection.GetSteamGameById(id);
        if (steamInfo is not null)
        {
            var gameDir = JoinGamePath(steamInfo.GameRoot, pakDirectory);
            if (!OperatingSystem.IsWindows() && !Directory.Exists(gameDir))
                return null;

            Log.Debug("Found {GameName} in steam manifests", steamInfo.Name);
            return DirectorySettings.Default(steamInfo.Name, gameDir, ue: ueVersion, aes: aesKey);
        }

        return null;
    }

    private DirectorySettings GetRockstarGamesGame(string key, string pakDirectory, EGame ueVersion)
    {
        if (!OperatingSystem.IsWindows())
            return null;

        var installLocation = string.Empty;
        try
        {
            installLocation = App.GetRegistryValue(@$"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\{key}", "InstallLocation", Microsoft.Win32.RegistryHive.LocalMachine);
        }
        catch
        {
            // ignored
        }

        var gameDir = $"{installLocation}{pakDirectory}";
        if (Directory.Exists(gameDir))
        {
            Log.Debug("Found {GameName} in the registry", key);
            return DirectorySettings.Default(key, gameDir, ue: ueVersion);
        }

        return null;
    }

    private DirectorySettings GetLevelInfiniteGame(string key, string pakDirectory, EGame ueVersion)
    {
        if (!OperatingSystem.IsWindows())
            return null;

        var installLocation = string.Empty;
        var displayName = string.Empty;

        try
        {
            installLocation = App.GetRegistryValue($@"Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\{key}", "GameInstallPath", Microsoft.Win32.RegistryHive.CurrentUser);
            displayName = App.GetRegistryValue($@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{key}", "DisplayName", Microsoft.Win32.RegistryHive.CurrentUser);
        }
        catch
        {
            // ignored
        }

        var gameDir = $"{installLocation}{pakDirectory}";
        if (Directory.Exists(gameDir))
        {
            Log.Debug("Found {GameName} in the registry", key);
            return DirectorySettings.Default(displayName, gameDir, ue: ueVersion);
        }

        return null;
    }

    private T GetDriveLauncherInstalls<T>(string jsonFile)
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            var launcher = $"{drive.Name}{jsonFile}";
            if (!File.Exists(launcher)) continue;

            Log.Debug("\"{Launcher}\" found in drive \"{DriveName}\"", launcher, drive.Name);
            return JsonConvert.DeserializeObject<T>(File.ReadAllText(launcher));
        }

        return default;
    }

#pragma warning disable 649
    private class LauncherInstalled
    {
        public Installation[] InstallationList;
    }

    private class Installation
    {
        public string InstallLocation;
        public string AppName;
        public string AppVersion;

        [JsonIgnore] public string Source;
    }

    /// <summary>Entry of legendary's / Heroic's installed.json (keyed by app name).</summary>
    private class LegendaryInstalledGame
    {
        [JsonProperty("app_name", NullValueHandling = NullValueHandling.Ignore)]
        public string AppName;

        [JsonProperty("install_path", NullValueHandling = NullValueHandling.Ignore)]
        public string InstallPath;

        [JsonProperty("title", NullValueHandling = NullValueHandling.Ignore)]
        public string Title;

        [JsonProperty("version", NullValueHandling = NullValueHandling.Ignore)]
        public string Version;
    }

    private class RiotClientInstalls
    {
        [JsonProperty("associated_client", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, string> AssociatedClient;

        [JsonProperty("patchlines", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, string> Patchlines;

        [JsonProperty("rc_default", NullValueHandling = NullValueHandling.Ignore)]
        public string RcDefault;

        [JsonProperty("rc_live", NullValueHandling = NullValueHandling.Ignore)]
        public string RcLive;
    }

    private class LauncherSettings
    {
        [JsonProperty("channel", NullValueHandling = NullValueHandling.Ignore)]
        public string Channel;

        [JsonProperty("customChannels", NullValueHandling = NullValueHandling.Ignore)]
        public object[] CustomChannels;

        [JsonProperty("deviceId", NullValueHandling = NullValueHandling.Ignore)]
        public string DeviceId;

        [JsonProperty("formatVersion", NullValueHandling = NullValueHandling.Ignore)]
        public int FormatVersion;

        [JsonProperty("locale", NullValueHandling = NullValueHandling.Ignore)]
        public string Locale;

        [JsonProperty("productLibraryDir", NullValueHandling = NullValueHandling.Ignore)]
        public string ProductLibraryDir;
    }
#pragma warning restore 649

    // https://stackoverflow.com/questions/54767662/finding-game-launcher-executables-in-directory-c-sharp/67679123#67679123
    public static class SteamDetection
    {
        private static readonly List<AppInfo> _steamApps;

        static SteamDetection()
        {
            try
            {
                _steamApps = GetSteamApps(GetSteamLibs());
            }
            catch (Exception e)
            {
                Log.Warning(e, "Failed to enumerate steam libraries");
                _steamApps = [];
            }
        }

        public static AppInfo GetSteamGameById(int id) => _steamApps.FirstOrDefault(app => app.Id == id.ToString());

        private static List<AppInfo> GetSteamApps(IEnumerable<string> steamLibs)
        {
            var apps = new List<AppInfo>();
            foreach (var files in steamLibs
                         .Select(GetSteamAppsDirectory)
                         .Select(appMetaDataPath => Directory.Exists(appMetaDataPath) ? Directory.GetFiles(appMetaDataPath, "*.acf") : null)
                         .Where(files => files != null))
            {
                apps.AddRange(files.Select(GetAppInfo).Where(appInfo => appInfo != null));
            }

            return apps;
        }

        // Windows is case-insensitive, on Linux the folder is "steamapps" (older installs may still use "SteamApps")
        private static string GetSteamAppsDirectory(string lib)
        {
            if (OperatingSystem.IsWindows())
                return Path.Combine(lib, "SteamApps");

            var lower = Path.Combine(lib, "steamapps");
            return Directory.Exists(lower) ? lower : Path.Combine(lib, "SteamApps");
        }

        private static AppInfo GetAppInfo(string appMetaFile)
        {
            var fileDataLines = File.ReadAllLines(appMetaFile);
            var dic = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in fileDataLines)
            {
                var match = Regex.Match(line, @"\s*""(?<key>\w+)""\s+""(?<val>.*)""");
                if (!match.Success) continue;
                var key = match.Groups["key"].Value;
                var val = match.Groups["val"].Value;
                dic[key] = val;
            }

            if (!dic.TryGetValue("appid", out var appId) ||
                !dic.TryGetValue("name", out var name) ||
                !dic.TryGetValue("installDir", out var installDir)) return null;

            var path = Path.GetDirectoryName(appMetaFile) ?? "";
            var libGameRoot = Path.Combine(path, "common", installDir);

            return Directory.Exists(libGameRoot) ? new AppInfo { Id = appId, Name = name, GameRoot = libGameRoot } : null;
        }

        private static List<string> GetSteamLibs()
        {
            if (!OperatingSystem.IsWindows())
                return GetUnixSteamLibs();

            var steamPath = GetSteamPath();
            if (steamPath == null || !Directory.Exists(steamPath)) return new List<string>();
            var libraries = new List<string> { steamPath };

            var listFile = Path.Combine(steamPath, @"steamapps\libraryfolders.vdf");
            if (!File.Exists(listFile)) return new List<string>();
            var lines = File.ReadAllLines(listFile);
            foreach (var line in lines)
            {
                var match = Regex.Match(line, @"""(?<path>\w:\\\\.*)""");
                if (!match.Success) continue;
                var path = match.Groups["path"].Value.Replace(@"\\", @"\");
                if (Directory.Exists(path) && !libraries.Contains(path))
                {
                    libraries.Add(path);
                }
            }

            return libraries;
        }

        /// <summary>
        /// Steam roots on Linux (native, legacy symlinks, flatpak) and macOS; every root's libraryfolders.vdf lists all library paths.
        /// </summary>
        private static List<string> GetUnixSteamLibs()
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var roots = new[]
            {
                Path.Combine(home, ".steam", "steam"),
                Path.Combine(home, ".steam", "root"),
                Path.Combine(home, ".local", "share", "Steam"),
                Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam"),
                Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", "data", "Steam"),
                Path.Combine(home, "snap", "steam", "common", ".local", "share", "Steam"),
                Path.Combine(home, "Library", "Application Support", "Steam") // macOS
            };

            var libraries = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            void AddLibrary(string path)
            {
                if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;
                if (seen.Add(ResolveRealPath(path)))
                    libraries.Add(path);
            }

            foreach (var root in roots)
            {
                if (!Directory.Exists(root)) continue;
                AddLibrary(root);

                var listFile = Path.Combine(GetSteamAppsDirectory(root), "libraryfolders.vdf");
                if (!File.Exists(listFile)) continue;

                foreach (var line in File.ReadAllLines(listFile))
                {
                    // new format: "path"		"/mnt/games/SteamLibrary" | old format: "1"		"/mnt/games/SteamLibrary"
                    var match = Regex.Match(line, @"^\s*""(?<key>path|\d+)""\s+""(?<path>[^""]+)""");
                    if (!match.Success) continue;

                    var path = match.Groups["path"].Value.Replace(@"\\", @"\");
                    if (!path.StartsWith('/')) continue;
                    AddLibrary(path);
                }
            }

            return libraries;
        }

        private static string ResolveRealPath(string path)
        {
            try
            {
                var full = Path.GetFullPath(path).TrimEnd('/');
                var info = new DirectoryInfo(full);
                var target = info.ResolveLinkTarget(true);
                return (target?.FullName ?? full).TrimEnd('/');
            }
            catch
            {
                return path;
            }
        }

        private static string GetSteamPath()
        {
            if (!OperatingSystem.IsWindows()) return null;
            return (string) Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", ""); // Win64, we don't support Win32
        }

        public class AppInfo
        {
            public string Id { get; internal set; }
            public string Name { get; internal set; }
            public string GameRoot { get; internal set; }

            public override string ToString()
            {
                return $"{Name} ({Id})";
            }
        }
    }
}
