using System.Runtime.InteropServices;

namespace DoorbellSvc.Configuration;

/// <summary>
///     Configuration settings for the doorbell service
/// </summary>
public sealed record DoorbellConfiguration
{
    // Audio constants
    public const int DefaultVolume = 80; // 0..200
    public const double MaxGainDb = 6.0; // clamp softvol gain
    public const uint SampleRate = 48000;
    public const uint Channels = 2; // stereo
    public const uint TargetLatencyUs = 50_000; // 50ms

    // Safety caps
    public const int MaxJsonBytes = 64 * 1024;
    public const int MaxRepeat = 50;
    public const int MaxDelayMs = 60_000;
    public const int MaxWavBuffer = 192_000; // bytes
    public const long MaxWavSize = 25L * 1024 * 1024;

    // Default paths
    public const string DefaultSoundsDir = "/var/lib/doorbell/sounds";
    public const string DefaultCacheDir = "/var/lib/doorbell/cache";
    public const string DefaultPcmName = "doorbell_out"; // ALSA softvol PCM
    public const string DefaultSoftvolCtrlName = "Doorbell Gain";
    public const string DefaultSocketPath = "/run/doorbell.sock";
    public const string DefaultLogDir = "/var/log/doorbelld";
    public const string LogFileName = "doorbelld.log";

    // Instance properties - can be overridden via environment variables
    public required string SoundsDirectory { get; init; }
    public required string CacheDirectory { get; init; }
    public required string PcmName { get; init; }
    public required string SoftvolControlName { get; init; }
    public required string SocketPath { get; init; }
    public required string LogDirectory { get; init; }
    public required int CardIndex { get; init; }

    public static DoorbellConfiguration FromEnvironment()
    {
        var cardIndex = int.TryParse(Environment.GetEnvironmentVariable("DOORBELL_CARD"), out var ci) ? ci : 1;
        var pcmName = Environment.GetEnvironmentVariable("DOORBELL_DEVICE") ?? DefaultPcmName;
        var soundsDir = Environment.GetEnvironmentVariable("DOORBELL_SOUNDS_DIR") ?? DefaultSoundsDir;
        var cacheDir = Environment.GetEnvironmentVariable("DOORBELL_CACHE_DIR") ?? DefaultCacheDir;
        var socketPath = Environment.GetEnvironmentVariable("DOORBELL_SOCKET") ?? DefaultSocketPath;
        var logDir = Environment.GetEnvironmentVariable("DOORBELL_LOG_DIR") ?? DefaultLogDir;

        return new DoorbellConfiguration
        {
            SoundsDirectory = soundsDir,
            CacheDirectory = cacheDir,
            PcmName = pcmName,
            SoftvolControlName = DefaultSoftvolCtrlName,
            SocketPath = socketPath,
            LogDirectory = logDir,
            CardIndex = cardIndex
        };
    }

    /// <summary>
    ///     Get the effective UID for logging purposes
    /// </summary>
    public static int GetEffectiveUserId()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return (int) geteuid();
            }
        }
        catch
        {
            // Fallback to hash of username
        }

        return unchecked(Environment.UserName.GetHashCode());
    }

    [DllImport("c")]
    private static extern uint geteuid();
}