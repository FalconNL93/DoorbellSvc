using DoorbellSvc.Audio;
using DoorbellSvc.Configuration;
using DoorbellSvc.Logging;

namespace DoorbellSvc.Core;

/// <summary>
///     Audio playback service implementation using ALSA
/// </summary>
public sealed class AudioPlaybackService : IAudioPlaybackService
{
    private readonly DoorbellConfiguration _configuration;
    private readonly AudioMixer _mixer;
    private readonly PcmAudioPlayer _pcmPlayer;
    private readonly Lock _playLock = new();
    private bool _disposed;
    private bool _isBusy;

    public AudioPlaybackService(DoorbellConfiguration configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _pcmPlayer = new PcmAudioPlayer(_configuration.PcmName, _configuration.CardIndex);
        _mixer = new AudioMixer(_configuration.CardIndex);
    }

    public string DeviceName => _pcmPlayer.DeviceName;

    /// <summary>
    ///     Play a sound file with specified parameters
    /// </summary>
    public bool PlaySound(string fileName, int volume, int repeat, int delayMs, bool allowQueue)
    {
        ThrowIfDisposed();

        var shouldPlay = true;
        lock (_playLock)
        {
            if (_isBusy && !allowQueue)
            {
                shouldPlay = false;
            }
            else if (!_isBusy)
            {
                _isBusy = true;
            }
        }

        if (!shouldPlay)
        {
            return false;
        }

        try
        {
            PlaySoundInternal(fileName, volume, repeat, delayMs);
            return true;
        }
        finally
        {
            lock (_playLock)
            {
                _isBusy = false;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _mixer?.Dispose();
        _pcmPlayer?.Dispose();
    }

    private void PlaySoundInternal(string baseFileName, int volume, int repeat, int delayMs)
    {
        var soundPath = Path.Combine(_configuration.SoundsDirectory, baseFileName);
        if (!File.Exists(soundPath))
        {
            throw new FileNotFoundException($"Sound not found: {baseFileName}", soundPath);
        }

        var playPath = soundPath;
        if (!baseFileName.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
        {
            var cachedPath = Path.Combine(_configuration.CacheDirectory,
                Path.GetFileNameWithoutExtension(baseFileName) + ".wav");
            if (File.Exists(cachedPath))
            {
                playPath = cachedPath;
            }
        }

        var (mixerPercent, softvolDb) = AudioMixer.MapVolume(volume);
        _mixer.SetPlaybackDecibels(_configuration.SoftvolControlName, softvolDb);
        _mixer.SetPlaybackPercent("Master", mixerPercent);
        _mixer.SetPlaybackPercent("Speaker", mixerPercent);

        PlayWavFile(playPath, repeat, delayMs);

        _mixer.MutePlayback("Speaker");
        _mixer.MutePlayback("Master");

        var volumeText = softvolDb > 0
            ? $"{volume}% (mixer {mixerPercent}% + softvol +{softvolDb:F2} dB)"
            : $"{volume}%";

        BackgroundLogger.Info($"Played sound {Path.GetFileName(playPath)} at {volumeText}, " +
                              $"repeat={repeat}, delay={delayMs}ms, device={DeviceName}");
    }

    private void PlayWavFile(string wavPath, int repeat, int delayMs)
    {
        using var fileStream = File.OpenRead(wavPath);
        if (fileStream.Length > DoorbellConfiguration.MaxWavSize)
        {
            throw new InvalidOperationException("WAV file too large");
        }

        var (isValid, dataOffset, dataLength) = AudioFileManager.ReadWavHeader(fileStream);
        if (!isValid)
        {
            throw new InvalidOperationException("Unsupported/corrupt WAV (expect PCM S16LE/48k/stereo)");
        }

        if ((long) dataOffset + dataLength > fileStream.Length)
        {
            throw new InvalidOperationException("WAV data chunk exceeds file length");
        }

        var buffer = new byte[Math.Min(DoorbellConfiguration.MaxWavBuffer, dataLength)];
        var repeatCount = Math.Max(1, repeat);

        for (var iteration = 0; iteration < repeatCount; iteration++)
        {
            fileStream.Position = dataOffset;
            var remainingBytes = dataLength;

            while (remainingBytes > 0)
            {
                var bytesToRead = Math.Min(remainingBytes, buffer.Length);
                var bytesRead = fileStream.Read(buffer, 0, bytesToRead);
                if (bytesRead <= 0)
                {
                    break;
                }

                _pcmPlayer.WriteFrames(new ReadOnlySpan<byte>(buffer, 0, bytesRead));
                remainingBytes -= bytesRead;
            }

            if (iteration + 1 < repeatCount && delayMs > 0)
            {
                Thread.Sleep(delayMs);
            }
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(AudioPlaybackService));
        }
    }
}
