using DoorbellSvc.Configuration;

namespace DoorbellSvc.Protocol;

/// <summary>
///     Message DTO for doorbell service communication
/// </summary>
public sealed record DoorbellMessage
{
    public string Cmd { get; init; } = string.Empty; // "play" | "ping" | "prewarm"
    public string File { get; init; } = string.Empty;
    public int Volume { get; init; } = DoorbellConfiguration.DefaultVolume; // 0..200
    public int Repeat { get; init; } = 1;
    public int DelayMs { get; init; } = 0;
    public int Queue { get; init; } = 0; // 1=wait if busy; 0=skip
    public int Ffmpeg { get; init; } = 1; // prewarm: 1 convert, 0 scan-only
}

/// <summary>
///     Response messages for various operations
/// </summary>
public static class DoorbellResponses
{
    public static object Success()
    {
        return new {ok = true};
    }

    public static object Error(string message)
    {
        return new {ok = false, error = message};
    }

    public static object ErrorWithDetail(string message, string detail)
    {
        return new {ok = false, error = message, detail};
    }

    public static object Busy()
    {
        return new {ok = false, reason = "busy"};
    }

    public static object Ping()
    {
        return new {ok = true, pong = 1};
    }

    public static object PrewarmResult(int total, int built, int upToDate, int failed)
    {
        return new {ok = true, total, built, up_to_date = upToDate, failed};
    }
}