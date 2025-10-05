using System.Collections.Concurrent;
using System.Text;
using DoorbellSvc.Configuration;

namespace DoorbellSvc.Logging;

/// <summary>
///     Background logger with automatic fallback to user-writable directories
/// </summary>
public sealed class BackgroundLogger : IDisposable
{
    private static readonly BlockingCollection<string> MessageQueue = new(new ConcurrentQueue<string>(), 8192);
    private static Thread? _loggerThread;
    private static FileStream? _fileStream;
    private static StreamWriter? _streamWriter;
    private static bool _isDisposed;

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        Stop();

        try
        {
            _streamWriter?.Dispose();
            _fileStream?.Dispose();
        }
        catch
        {
            // ignore
        }
    }

    /// <summary>
    ///     Initialize the logger with automatic path resolution
    /// </summary>
    public static void Initialize(string? preferredLogDir = null)
    {
        if (_loggerThread != null)
        {
            return;
        }

        var logPath = ResolveWritableLogPath(preferredLogDir);
        var directory = Path.GetDirectoryName(logPath)!;
        Directory.CreateDirectory(directory);

        _fileStream = new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.Read,
            262_144, FileOptions.WriteThrough);
        _streamWriter = new StreamWriter(_fileStream, new UTF8Encoding(false)) {AutoFlush = false};

        _loggerThread = new Thread(ProcessLogMessages)
        {
            IsBackground = true,
            Name = "doorbelld-logger"
        };
        _loggerThread.Start();

        Info("==== doorbelld started ====");
        Info($"logging to: {logPath}");
    }

    /// <summary>
    ///     Log an informational message
    /// </summary>
    public static void Info(string message)
    {
        if (_isDisposed)
        {
            return;
        }

        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
        if (!MessageQueue.TryAdd(line))
        {
            MessageQueue.Add(line);
        }
    }

    /// <summary>
    ///     Stop the logger and flush all pending messages
    /// </summary>
    public static void Stop()
    {
        try
        {
            MessageQueue.CompleteAdding();
        }
        catch
        {
            // ignore
        }

        try
        {
            _loggerThread?.Join(1000);
        }
        catch
        {
            // ignore
        }
    }

    private static void ProcessLogMessages()
    {
        try
        {
            foreach (var line in MessageQueue.GetConsumingEnumerable())
            {
                _streamWriter!.WriteLine(line);
                _streamWriter.Flush();
            }
        }
        catch
        {
            // ignore
        }
        finally
        {
            try
            {
                _streamWriter?.Flush();
                _streamWriter?.Dispose();
                _fileStream?.Dispose();
            }
            catch
            {
                // ignore
            }
        }
    }

    /// <summary>
    ///     Resolve a writable log path with fallback options
    /// </summary>
    private static string ResolveWritableLogPath(string? preferredLogDir)
    {
        var uid = DoorbellConfiguration.GetEffectiveUserId();
        var exeDir = AppContext.BaseDirectory?.TrimEnd('/') ?? "/tmp";
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var xdgState = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
        var xdgCache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");

        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(preferredLogDir))
        {
            candidates.Add(Path.Combine(preferredLogDir, DoorbellConfiguration.LogFileName));
        }

        if (!string.IsNullOrWhiteSpace(xdgState))
        {
            candidates.Add(Path.Combine(xdgState, "doorbelld", DoorbellConfiguration.LogFileName));
        }

        if (!string.IsNullOrWhiteSpace(home))
        {
            candidates.Add(Path.Combine(home, ".local", "state", "doorbelld", DoorbellConfiguration.LogFileName));
        }

        if (!string.IsNullOrWhiteSpace(xdgCache))
        {
            candidates.Add(Path.Combine(xdgCache, "doorbelld", DoorbellConfiguration.LogFileName));
        }

        candidates.Add(Path.Combine(exeDir, "logs", DoorbellConfiguration.LogFileName));

        candidates.Add(Path.Combine("/tmp", $"doorbelld-{uid}", DoorbellConfiguration.LogFileName));

        foreach (var path in candidates)
        {
            if (TryCreateLogFile(path))
            {
                return path;
            }
        }

        var fallback = Path.Combine(exeDir, DoorbellConfiguration.LogFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(fallback)!);
        return fallback;
    }

    private static bool TryCreateLogFile(string path)
    {
        try
        {
            var directory = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(directory);

            using var testStream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
            var probe = Encoding.UTF8.GetBytes($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] ==== log probe ====\n");
            testStream.Write(probe, 0, probe.Length);

            return true;
        }
        catch
        {
            return false;
        }
    }
}
