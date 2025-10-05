using DoorbellSvc.Configuration;

namespace DoorbellSvc.Audio;

/// <summary>
///     PCM audio player using ALSA
/// </summary>
public sealed class PcmAudioPlayer : IDisposable
{
    private bool _disposed;
    private IntPtr _pcmHandle = IntPtr.Zero;

    public PcmAudioPlayer(string preferredDevice, int cardIndex)
    {
        InitializePcm(preferredDevice, cardIndex);
    }

    public string DeviceName { get; private set; } = string.Empty;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_pcmHandle != IntPtr.Zero)
        {
            AlsaInterop.snd_pcm_drain(_pcmHandle);
            AlsaInterop.snd_pcm_close(_pcmHandle);
            _pcmHandle = IntPtr.Zero;
        }

        _disposed = true;
    }

    /// <summary>
    ///     Write interleaved S16LE audio frames to the PCM device
    /// </summary>
    public void WriteFrames(ReadOnlySpan<byte> interleavedS16Data)
    {
        ThrowIfDisposed();

        var frameCount = (ulong) (interleavedS16Data.Length / (DoorbellConfiguration.Channels * 2));
        if (frameCount == 0)
        {
            return;
        }

        unsafe
        {
            fixed (byte* dataPtr = interleavedS16Data)
            {
                var result = AlsaInterop.snd_pcm_writei(_pcmHandle, (IntPtr) dataPtr, frameCount);
                if (result < 0)
                {
                    // Try to recover from underrun
                    AlsaInterop.snd_pcm_prepare(_pcmHandle);
                    AlsaInterop.snd_pcm_writei(_pcmHandle, (IntPtr) dataPtr, frameCount);
                }
            }
        }
    }

    private void InitializePcm(string preferredDevice, int cardIndex)
    {
        var deviceName = preferredDevice;
        var result = AlsaInterop.snd_pcm_open(out _pcmHandle, deviceName, AlsaInterop.SND_PCM_STREAM_PLAYBACK, 0);

        if (result < 0)
        {
            // Fallback to hardware device
            deviceName = $"hw:{cardIndex},0";
            result = AlsaInterop.snd_pcm_open(out _pcmHandle, deviceName, AlsaInterop.SND_PCM_STREAM_PLAYBACK, 0);
        }

        AlsaInterop.CheckResult(result, "snd_pcm_open");

        result = AlsaInterop.snd_pcm_set_params(_pcmHandle,
            AlsaInterop.SND_PCM_FORMAT_S16_LE,
            AlsaInterop.SND_PCM_ACCESS_RW_INTERLEAVED,
            DoorbellConfiguration.Channels,
            DoorbellConfiguration.SampleRate,
            1, // soft_resample
            DoorbellConfiguration.TargetLatencyUs);

        AlsaInterop.CheckResult(result, "snd_pcm_set_params");

        DeviceName = deviceName;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(PcmAudioPlayer));
        }
    }
}
