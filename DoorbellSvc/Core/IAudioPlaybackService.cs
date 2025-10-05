namespace DoorbellSvc.Core;

/// <summary>
///     Interface for audio playback services
/// </summary>
public interface IAudioPlaybackService : IDisposable
{
    /// <summary>
    ///     The name of the audio device being used
    /// </summary>
    string DeviceName { get; }

    /// <summary>
    ///     Play a sound file with specified parameters
    /// </summary>
    /// <param name="fileName">Base filename in the sounds directory</param>
    /// <param name="volume">Volume level (0-200%)</param>
    /// <param name="repeat">Number of times to repeat</param>
    /// <param name="delayMs">Delay between repeats in milliseconds</param>
    /// <param name="allowQueue">Whether to queue if currently busy</param>
    /// <returns>True if sound was played, false if busy and not queued</returns>
    bool PlaySound(string fileName, int volume, int repeat, int delayMs, bool allowQueue);
}