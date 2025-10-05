using System.Runtime.InteropServices;

namespace DoorbellSvc.Audio;

/// <summary>
///     ALSA (Advanced Linux Sound Architecture) native interop
/// </summary>
internal static class AlsaInterop
{
    private const string LibraryName = "libasound.so.2";

    // PCM constants
    public const int SND_PCM_STREAM_PLAYBACK = 0;
    public const int SND_PCM_FORMAT_S16_LE = 2;
    public const int SND_PCM_ACCESS_RW_INTERLEAVED = 3;

    // PCM functions
    [DllImport(LibraryName)]
    public static extern int snd_pcm_open(out IntPtr pcm, string name, int stream, int mode);

    [DllImport(LibraryName)]
    public static extern int snd_pcm_close(IntPtr pcm);

    [DllImport(LibraryName)]
    public static extern int snd_pcm_set_params(IntPtr pcm, int format, int access, uint channels, uint rate, int soft_resample, uint latency_us);

    [DllImport(LibraryName)]
    public static extern int snd_pcm_prepare(IntPtr pcm);

    [DllImport(LibraryName)]
    public static extern long snd_pcm_writei(IntPtr pcm, IntPtr buffer, ulong frames);

    [DllImport(LibraryName)]
    public static extern int snd_pcm_drain(IntPtr pcm);

    // Mixer functions
    [DllImport(LibraryName)]
    public static extern int snd_mixer_open(out IntPtr handle, int mode);

    [DllImport(LibraryName)]
    public static extern int snd_mixer_close(IntPtr handle);

    [DllImport(LibraryName)]
    public static extern int snd_mixer_attach(IntPtr handle, string name);

    [DllImport(LibraryName)]
    public static extern int snd_mixer_selem_register(IntPtr handle, IntPtr options, IntPtr classp);

    [DllImport(LibraryName)]
    public static extern int snd_mixer_load(IntPtr handle);

    [DllImport(LibraryName)]
    public static extern int snd_mixer_selem_id_malloc(out IntPtr ptr);

    [DllImport(LibraryName)]
    public static extern void snd_mixer_selem_id_free(IntPtr ptr);

    [DllImport(LibraryName)]
    public static extern void snd_mixer_selem_id_set_name(IntPtr obj, string name);

    [DllImport(LibraryName)]
    public static extern void snd_mixer_selem_id_set_index(IntPtr obj, uint index);

    [DllImport(LibraryName)]
    public static extern IntPtr snd_mixer_find_selem(IntPtr handle, IntPtr id);

    [DllImport(LibraryName)]
    public static extern int snd_mixer_selem_get_playback_volume_range(IntPtr elem, out long min, out long max);

    [DllImport(LibraryName)]
    public static extern int snd_mixer_selem_set_playback_volume_all(IntPtr elem, long value);

    [DllImport(LibraryName)]
    public static extern int snd_mixer_selem_set_playback_switch_all(IntPtr elem, int val);

    [DllImport(LibraryName)]
    public static extern int snd_mixer_selem_get_playback_dB_range(IntPtr elem, out long min, out long max);

    [DllImport(LibraryName)]
    public static extern int snd_mixer_selem_set_playback_dB_all(IntPtr elem, long value, int dir);

    // Error handling
    [DllImport(LibraryName)]
    public static extern IntPtr snd_strerror(int errnum);

    /// <summary>
    ///     Get human-readable error message from ALSA error code
    /// </summary>
    public static string GetErrorMessage(int errorCode)
    {
        return Marshal.PtrToStringAnsi(snd_strerror(errorCode)) ?? $"err{errorCode}";
    }

    /// <summary>
    ///     Check ALSA return code and throw exception on error
    /// </summary>
    public static void CheckResult(int returnCode, string operation)
    {
        if (returnCode < 0)
        {
            throw new InvalidOperationException($"{operation}: {GetErrorMessage(returnCode)}");
        }
    }
}
