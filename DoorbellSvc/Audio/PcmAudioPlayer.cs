using DoorbellSvc.Configuration;
using static DoorbellSvc.Audio.AlsaInterop;

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
            snd_pcm_drain(_pcmHandle);
            snd_pcm_close(_pcmHandle);
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
                var result = snd_pcm_writei(_pcmHandle, (IntPtr) dataPtr, frameCount);
                if (result >= 0)
                {
                    return;
                }

                snd_pcm_prepare(_pcmHandle);
                snd_pcm_writei(_pcmHandle, (IntPtr) dataPtr, frameCount);
            }
        }
    }

    private void InitializePcm(string preferredDevice, int cardIndex)
    {
        var deviceName = preferredDevice;
        var result = snd_pcm_open(out _pcmHandle, deviceName, SND_PCM_STREAM_PLAYBACK, 0);

        if (result < 0)
        {
            deviceName = $"hw:{cardIndex},0";
            result = snd_pcm_open(out _pcmHandle, deviceName, SND_PCM_STREAM_PLAYBACK, 0);
        }

        CheckResult(result, "snd_pcm_open");

        result = snd_pcm_set_params(_pcmHandle,
            SND_PCM_FORMAT_S16_LE,
            SND_PCM_ACCESS_RW_INTERLEAVED,
            DoorbellConfiguration.Channels,
            DoorbellConfiguration.SampleRate,
            1,
            DoorbellConfiguration.TargetLatencyUs);

        CheckResult(result, "snd_pcm_set_params");

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
