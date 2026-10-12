using System;
using System.Collections.Generic;
using System.Linq;
using OpenTK.Audio.OpenAL;
using Serilog;

namespace FModel.Framework.Audio;

/// <summary>An audio output device (replacement of CSCore.CoreAudioAPI.MMDevice). <see cref="DeviceId"/> is the OpenAL device name.</summary>
public sealed record AudioDevice(string FriendlyName, string DeviceId, bool IsDefault = false)
{
    public override string ToString() => FriendlyName;

    /// <summary>Enumerates the OpenAL playback devices. Returns an empty list if OpenAL is not available.</summary>
    public static IReadOnlyList<AudioDevice> Enumerate()
    {
        if (!OpenAlRuntime.IsAvailable)
            return [];

        try
        {
            lock (OpenAlRuntime.Lock)
            {
                IEnumerable<string> names;
                string defaultName;
                // note: ALC.EnumerateAll.GetString(...) corrupts the heap in OpenTK 4.9.4, use the plain ALC overloads
                if (ALC.IsExtensionPresent(ALDevice.Null, "ALC_ENUMERATE_ALL_EXT"))
                {
                    names = ALC.GetString(ALDevice.Null, AlcGetStringList.AllDevicesSpecifier);
                    defaultName = ALC.GetString(ALDevice.Null, AlcGetString.DefaultAllDevicesSpecifier);
                }
                else
                {
                    names = ALC.GetStringList(GetEnumerationStringList.DeviceSpecifier);
                    defaultName = ALC.GetString(ALDevice.Null, AlcGetString.DefaultDeviceSpecifier);
                }

                return names
                    .Where(n => !string.IsNullOrEmpty(n))
                    .Distinct()
                    .Select(n => new AudioDevice(GetFriendlyName(n), n, n == defaultName))
                    .ToList();
            }
        }
        catch (Exception e)
        {
            Log.Error(e, "Failed to enumerate audio devices");
            return [];
        }
    }

    private static string GetFriendlyName(string name)
    {
        const string prefix = "OpenAL Soft on ";
        return name.StartsWith(prefix, StringComparison.Ordinal) && name.Length > prefix.Length ? name[prefix.Length..] : name;
    }
}

/// <summary>Loads the native OpenAL library (libopenal.so.1 / soft_oal.dll / OpenAL32.dll) on first use.</summary>
public static class OpenAlRuntime
{
    /// <summary>OpenAL contexts are process wide: every AL/ALC call is made under this lock.</summary>
    public static readonly object Lock = new();

    private static bool? _isAvailable;

    /// <summary>Why OpenAL could not be loaded (null when available).</summary>
    public static string Error { get; private set; }

    public static bool IsAvailable
    {
        get
        {
            lock (Lock)
            {
                if (_isAvailable.HasValue)
                    return _isAvailable.Value;

                try
                {
                    ALC.GetString(ALDevice.Null, AlcGetString.Extensions);
                    _isAvailable = true;
                }
                catch (Exception e)
                {
                    _isAvailable = false;
                    Error = (e.InnerException ?? e).Message;
                    Log.Error(e, "OpenAL could not be loaded, audio playback is disabled (install OpenAL Soft: libopenal1 / openal-soft)");
                }

                return _isAvailable.Value;
            }
        }
    }
}
