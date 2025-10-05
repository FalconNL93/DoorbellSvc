using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using DoorbellSvc.Audio;
using DoorbellSvc.Configuration;
using DoorbellSvc.Logging;
using DoorbellSvc.Protocol;

namespace DoorbellSvc.Core;

/// <summary>
///     Unix domain socket server for handling doorbell commands
/// </summary>
public sealed class DoorbellSocketServer : IDisposable
{
    private readonly IAudioPlaybackService _audioService;
    private readonly DoorbellConfiguration _configuration;
    private bool _disposed;
    private Socket? _listenerSocket;

    public DoorbellSocketServer(DoorbellConfiguration configuration, IAudioPlaybackService audioService)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _audioService = audioService ?? throw new ArgumentNullException(nameof(audioService));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Console.WriteLine($"Shutting down socket server on {_configuration.SocketPath}");
        _disposed = true;
        _listenerSocket?.Dispose();

        try
        {
            if (File.Exists(_configuration.SocketPath))
            {
                File.Delete(_configuration.SocketPath);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to delete socket file: {ex.Message}");
        }
    }

    /// <summary>
    ///     Start the socket server and listen for connections
    /// </summary>
    public void Start()
    {
        ThrowIfDisposed();

        try
        {
            var dir = Path.GetDirectoryName(_configuration.SocketPath)!;
            Directory.CreateDirectory(dir);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Unable to create socket directory: {e.Message}");
            Environment.Exit(-1);
        }

        try
        {
            if (File.Exists(_configuration.SocketPath))
            {
                File.Delete(_configuration.SocketPath);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Unable to delete existing socket file: {ex.Message}");
            Environment.Exit(-1);
        }

        var endpoint = new UnixDomainSocketEndPoint(_configuration.SocketPath);
        _listenerSocket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        _listenerSocket.Bind(endpoint);

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            try
            {
                File.SetUnixFileMode(_configuration.SocketPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite |
                    UnixFileMode.GroupRead | UnixFileMode.GroupWrite);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unable to set unix file mode: {ex.Message}");
            }
        }

        _listenerSocket.Listen(16);

        Console.WriteLine($"Socket server running on {_configuration.SocketPath}");
        BackgroundLogger.Info($"daemon ready on {_configuration.SocketPath}");
        BackgroundLogger.Info($"PCM device: {_audioService.DeviceName}, card: {_configuration.CardIndex}");
    }

    /// <summary>
    ///     Accept and handle incoming client connections
    /// </summary>
    public void AcceptConnections()
    {
        ThrowIfDisposed();

        if (_listenerSocket == null)
        {
            throw new InvalidOperationException("Server not started. Call Start() first.");
        }

        while (!_disposed)
        {
            try
            {
                using var clientSocket = _listenerSocket.Accept();
                HandleClient(clientSocket);
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                BackgroundLogger.Info($"Accept error: {ex.Message}");
            }
        }
    }

    private void HandleClient(Socket clientSocket)
    {
        try
        {
            var message = ReceiveMessage(clientSocket);
            if (message == null)
            {
                SendResponse(clientSocket, DoorbellResponses.Error("empty msg"));
                return;
            }

            var response = message.Cmd switch
            {
                "play" => HandlePlayCommand(message),
                "prewarm" => HandlePrewarmCommand(message),
                "ping" => DoorbellResponses.Ping(),
                _ => DoorbellResponses.Error("unknown cmd")
            };

            SendResponse(clientSocket, response);
        }
        catch (Exception ex)
        {
            BackgroundLogger.Info($"handler error: {ex.Message}");
            try
            {
                SendResponse(clientSocket, DoorbellResponses.ErrorWithDetail("handler error", ex.Message));
            }
            catch
            {
                // ignore
            }
        }
    }

    private DoorbellMessage? ReceiveMessage(Socket socket)
    {
        using var memoryStream = new MemoryStream(1024);
        var buffer = new byte[2048];
        var totalBytes = 0;
        socket.ReceiveTimeout = 2000;

        while (true)
        {
            int bytesReceived;
            try
            {
                bytesReceived = socket.Receive(buffer);
            }
            catch (SocketException)
            {
                break;
            }

            if (bytesReceived <= 0)
            {
                break;
            }

            memoryStream.Write(buffer, 0, bytesReceived);
            totalBytes += bytesReceived;

            if (totalBytes > DoorbellConfiguration.MaxJsonBytes)
            {
                SendResponse(socket, DoorbellResponses.Error("request too large"));
                return null;
            }

            if (socket.Available == 0)
            {
                break;
            }
        }

        var messageData = memoryStream.GetBuffer().AsSpan(0, (int) memoryStream.Length);
        try
        {
            return JsonSerializer.Deserialize(messageData, DoorbellJsonContext.Default.DoorbellMessage);
        }
        catch (Exception ex)
        {
            SendResponse(socket, DoorbellResponses.ErrorWithDetail("bad json", ex.Message));
            return null;
        }
    }

    private DoorbellResponse HandlePlayCommand(DoorbellMessage message)
    {
        var fileName = Path.GetFileName(message.File ?? "");
        if (string.IsNullOrWhiteSpace(fileName) || !AudioFileManager.IsSafeFileName(fileName))
        {
            return DoorbellResponses.Error("invalid file");
        }

        var volume = ClampInt(message.Volume, 0, 200, DoorbellConfiguration.DefaultVolume);
        var repeat = ClampInt(message.Repeat, 1, DoorbellConfiguration.MaxRepeat, 1);
        var delay = ClampInt(message.DelayMs, 0, DoorbellConfiguration.MaxDelayMs, 0);

        try
        {
            var played = _audioService.PlaySound(fileName, volume, repeat, delay, message.Queue != 0);
            return played ? DoorbellResponses.Success() : DoorbellResponses.Busy();
        }
        catch (Exception ex)
        {
            BackgroundLogger.Info($"Play error: {ex.Message}");
            return DoorbellResponses.Error(ex.Message);
        }
    }

    private DoorbellResponse HandlePrewarmCommand(DoorbellMessage message)
    {
        var (total, built, upToDate, failed) = AudioFileManager.PrewarmCache(_configuration.SoundsDirectory,
            _configuration.CacheDirectory,
            message.Ffmpeg != 0);

        return DoorbellResponses.PrewarmResult(total, built, upToDate, failed);
    }

    private static void SendResponse(Socket socket, DoorbellResponse response)
    {
        try
        {
            var json = JsonSerializer.Serialize(response, typeof(DoorbellResponse), DoorbellJsonContext.Default);
            var data = Encoding.UTF8.GetBytes(json + "\n");
            socket.Send(data);
        }
        catch (Exception ex)
        {
            BackgroundLogger.Info($"SendResponse error: {ex.Message}");
        }
    }

    private static int ClampInt(int value, int min, int max, int fallback)
    {
        return value < min ? fallback : value > max ? max : value;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(DoorbellSocketServer));
        }
    }
}
