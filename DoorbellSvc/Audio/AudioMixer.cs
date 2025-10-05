using DoorbellSvc.Configuration;

namespace DoorbellSvc.Audio;

/// <summary>
///     ALSA mixer control for volume and mute operations
/// </summary>
public sealed class AudioMixer : IDisposable
{
    private readonly int _cardIndex;
    private readonly IntPtr _mixerHandle;
    private bool _disposed;

    public AudioMixer(int cardIndex)
    {
        _cardIndex = cardIndex;

        var result = AlsaInterop.snd_mixer_open(out _mixerHandle, 0);
        AlsaInterop.CheckResult(result, "mixer_open");

        result = AlsaInterop.snd_mixer_attach(_mixerHandle, $"hw:{_cardIndex}");
        AlsaInterop.CheckResult(result, "mixer_attach");

        result = AlsaInterop.snd_mixer_selem_register(_mixerHandle, IntPtr.Zero, IntPtr.Zero);
        AlsaInterop.CheckResult(result, "mixer_register");

        result = AlsaInterop.snd_mixer_load(_mixerHandle);
        AlsaInterop.CheckResult(result, "mixer_load");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_mixerHandle != IntPtr.Zero)
        {
            AlsaInterop.snd_mixer_close(_mixerHandle);
        }

        _disposed = true;
    }

    /// <summary>
    ///     Set playback volume as percentage (0-100%)
    /// </summary>
    public void SetPlaybackPercent(string controlName, int percent, bool unmute = true)
    {
        ThrowIfDisposed();

        var element = FindElement(controlName);
        if (element == IntPtr.Zero)
        {
            return;
        }

        if (AlsaInterop.snd_mixer_selem_get_playback_volume_range(element, out var min, out var max) == 0)
        {
            percent = Math.Clamp(percent, 0, 100);
            var value = min + (long) ((max - min) * (percent / 100.0));
            AlsaInterop.snd_mixer_selem_set_playback_volume_all(element, value);
        }

        if (unmute)
        {
            AlsaInterop.snd_mixer_selem_set_playback_switch_all(element, 1);
        }
    }

    /// <summary>
    ///     Set playback volume in decibels
    /// </summary>
    public void SetPlaybackDecibels(string controlName, double decibels)
    {
        ThrowIfDisposed();

        var element = FindElement(controlName);
        if (element == IntPtr.Zero)
        {
            return;
        }

        if (AlsaInterop.snd_mixer_selem_get_playback_dB_range(element, out var minDb, out var maxDb) == 0)
        {
            var hundredths = (long) Math.Round(decibels * 100.0);
            hundredths = Math.Clamp(hundredths, minDb, maxDb);
            AlsaInterop.snd_mixer_selem_set_playback_dB_all(element, hundredths, 0);
        }
    }

    /// <summary>
    ///     Mute playback for the specified control
    /// </summary>
    public void MutePlayback(string controlName)
    {
        ThrowIfDisposed();

        var element = FindElement(controlName);
        if (element == IntPtr.Zero)
        {
            return;
        }

        AlsaInterop.snd_mixer_selem_set_playback_switch_all(element, 0);

        if (AlsaInterop.snd_mixer_selem_get_playback_volume_range(element, out var min, out _) == 0)
        {
            AlsaInterop.snd_mixer_selem_set_playback_volume_all(element, min);
        }
    }

    /// <summary>
    ///     Map volume percentage to mixer and softvol settings
    /// </summary>
    public static (int mixerPercent, double softvolDecibels) MapVolume(int volumePercent)
    {
        var clampedVolume = Math.Clamp(volumePercent, 0, 200);

        if (clampedVolume <= 100)
        {
            return (clampedVolume, 0.0);
        }

        var decibels = 20.0 * Math.Log10(clampedVolume / 100.0);
        if (decibels > DoorbellConfiguration.MaxGainDb)
        {
            decibels = DoorbellConfiguration.MaxGainDb;
        }

        return (100, decibels);
    }

    private IntPtr FindElement(string name)
    {
        var result = AlsaInterop.snd_mixer_selem_id_malloc(out var id);
        if (result < 0)
        {
            return IntPtr.Zero;
        }

        try
        {
            AlsaInterop.snd_mixer_selem_id_set_name(id, name);
            AlsaInterop.snd_mixer_selem_id_set_index(id, 0);
            return AlsaInterop.snd_mixer_find_selem(_mixerHandle, id);
        }
        finally
        {
            AlsaInterop.snd_mixer_selem_id_free(id);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(AudioMixer));
        }
    }
}