using DoorbellSvc.Audio;
using DoorbellSvc.Configuration;
using DoorbellSvc.Logging;

namespace DoorbellSvc.Core;

/// <summary>
///     Main doorbell service orchestrating audio playback and socket communication
/// </summary>
public sealed class DoorbellServiceHost : IDisposable
{
    private readonly AudioPlaybackService _audioService;
    private readonly DoorbellConfiguration _configuration;
    private readonly DoorbellSocketServer _socketServer;
    private bool _disposed;

    public DoorbellServiceHost()
    {
        _configuration = DoorbellConfiguration.FromEnvironment();
        // Prewarm audio cache at startup
        AudioFileManager.PrewarmCache(_configuration.SoundsDirectory,
            _configuration.CacheDirectory,
            true // enable ffmpeg
        );
        _audioService = new AudioPlaybackService(_configuration);
        _socketServer = new DoorbellSocketServer(_configuration, _audioService);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _socketServer?.Dispose();
        _audioService?.Dispose();
        BackgroundLogger.Stop();
    }

    /// <summary>
    ///     Start the doorbell service
    /// </summary>
    public void Run()
    {
        ThrowIfDisposed();

        // Initialize logging
        BackgroundLogger.Initialize(_configuration.LogDirectory);

        try
        {
            _socketServer.Start();
            _socketServer.AcceptConnections();
        }
        catch (Exception ex)
        {
            BackgroundLogger.Info($"fatal: {ex.Message}");
            throw;
        }
        finally
        {
            BackgroundLogger.Stop();
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(DoorbellServiceHost));
        }
    }
}
