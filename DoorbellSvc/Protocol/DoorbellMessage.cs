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
///     Base response class
/// </summary>
public abstract record DoorbellResponse
{
    public bool Ok { get; init; }
}

/// <summary>
///     Success response
/// </summary>
public sealed record SuccessResponse : DoorbellResponse
{
    public SuccessResponse()
    {
        Ok = true;
    }
}

/// <summary>
///     Error response
/// </summary>
public sealed record ErrorResponse : DoorbellResponse
{
    public ErrorResponse()
    {
        Ok = false;
    }

    public string Error { get; init; } = string.Empty;
    public string? Detail { get; init; }
    public string? Reason { get; init; }
}

/// <summary>
///     Ping response
/// </summary>
public sealed record PingResponse : DoorbellResponse
{
    public PingResponse()
    {
        Ok = true;
    }

    public int Pong { get; init; } = 1;
}

/// <summary>
///     Prewarm response
/// </summary>
public sealed record PrewarmResponse : DoorbellResponse
{
    public PrewarmResponse()
    {
        Ok = true;
    }

    public int Total { get; init; }
    public int Built { get; init; }
    public int UpToDate { get; init; }
    public int Failed { get; init; }
}

/// <summary>
///     Response factory methods
/// </summary>
public static class DoorbellResponses
{
    public static SuccessResponse Success()
    {
        return new SuccessResponse();
    }

    public static ErrorResponse Error(string message)
    {
        return new ErrorResponse {Error = message};
    }

    public static ErrorResponse ErrorWithDetail(string message, string detail)
    {
        return new ErrorResponse {Error = message, Detail = detail};
    }

    public static ErrorResponse Busy()
    {
        return new ErrorResponse {Reason = "busy"};
    }

    public static PingResponse Ping()
    {
        return new PingResponse();
    }

    public static PrewarmResponse PrewarmResult(int total, int built, int upToDate, int failed)
    {
        return new PrewarmResponse {Total = total, Built = built, UpToDate = upToDate, Failed = failed};
    }
}