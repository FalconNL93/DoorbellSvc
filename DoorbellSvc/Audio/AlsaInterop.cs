using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace DoorbellSvc.Audio;

/// <summary>
///     Provides P/Invoke bindings for the ALSA (Advanced Linux Sound Architecture) library (libasound.so.2).
/// </summary>
internal static partial class AlsaInterop
{
    private const string LibraryName = "libasound.so.2";

    /// <summary>PCM stream type for playback (see snd_pcm_stream_t in ALSA).</summary>
    public const int SND_PCM_STREAM_PLAYBACK = 0;

    /// <summary>PCM format: Signed 16-bit little-endian (see snd_pcm_format_t in ALSA).</summary>
    public const int SND_PCM_FORMAT_S16_LE = 2;

    /// <summary>PCM access type: interleaved read/write (see snd_pcm_access_t in ALSA).</summary>
    public const int SND_PCM_ACCESS_RW_INTERLEAVED = 3;

    /// <summary>
    ///     Opens a PCM device.
    ///     See: https://www.alsa-project.org/alsa-doc/alsa-lib/group___p_c_m.html#gaa2e2e6b2b1b1b1b1b1b1b1b1b1b1b1b1
    /// </summary>
    [LibraryImport(LibraryName,
        EntryPoint = "snd_pcm_open",
        StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int snd_pcm_open(out IntPtr pcm, string name, int stream, int mode);

    /// <summary>
    ///     Closes a PCM device handle.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "snd_pcm_close")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int snd_pcm_close(IntPtr pcm);

    /// <summary>
    ///     Sets hardware/software parameters for a PCM device in a simplified way.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "snd_pcm_set_params")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int snd_pcm_set_params(
        IntPtr pcm,
        int format,
        int access,
        uint channels,
        uint rate,
        int soft_resample,
        uint latency_us
    );

    /// <summary>
    ///     Prepares PCM for use after configuration.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "snd_pcm_prepare")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int snd_pcm_prepare(IntPtr pcm);

    /// <summary>
    ///     Writes interleaved frames to PCM device.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "snd_pcm_writei")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial long snd_pcm_writei(IntPtr pcm, IntPtr buffer, ulong frames);

    /// <summary>
    ///     Stops PCM playback after pending frames are played.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "snd_pcm_drain")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int snd_pcm_drain(IntPtr pcm);

    /// <summary>
    ///     Opens a mixer handle.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "snd_mixer_open")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int snd_mixer_open(out IntPtr handle, int mode);

    /// <summary>
    ///     Closes a mixer handle.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "snd_mixer_close")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int snd_mixer_close(IntPtr handle);

    /// <summary>
    ///     Attaches a mixer to a sound card.
    /// </summary>
    [LibraryImport(LibraryName,
        EntryPoint = "snd_mixer_attach",
        StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int snd_mixer_attach(IntPtr handle, string name);

    /// <summary>
    ///     Registers mixer simple element class.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "snd_mixer_selem_register")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int snd_mixer_selem_register(
        IntPtr handle,
        IntPtr options,
        IntPtr classp
    );

    /// <summary>
    ///     Loads mixer elements.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "snd_mixer_load")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int snd_mixer_load(IntPtr handle);

    /// <summary>
    ///     Allocates a simple element identifier.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "snd_mixer_selem_id_malloc")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int snd_mixer_selem_id_malloc(out IntPtr ptr);

    /// <summary>
    ///     Frees a simple element identifier.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "snd_mixer_selem_id_free")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void snd_mixer_selem_id_free(IntPtr ptr);

    /// <summary>
    ///     Sets the name of a simple element identifier.
    /// </summary>
    [LibraryImport(LibraryName,
        EntryPoint = "snd_mixer_selem_id_set_name",
        StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void snd_mixer_selem_id_set_name(IntPtr obj, string name);

    /// <summary>
    ///     Sets the index of a simple element identifier.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "snd_mixer_selem_id_set_index")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void snd_mixer_selem_id_set_index(IntPtr obj, uint index);

    /// <summary>
    ///     Finds a simple mixer element by identifier.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "snd_mixer_find_selem")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial IntPtr snd_mixer_find_selem(IntPtr handle, IntPtr id);

    /// <summary>
    ///     Gets the playback volume range for a mixer element.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "snd_mixer_selem_get_playback_volume_range")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int snd_mixer_selem_get_playback_volume_range(
        IntPtr elem,
        out long min,
        out long max
    );

    /// <summary>
    ///     Sets the playback volume for all channels of a mixer element.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "snd_mixer_selem_set_playback_volume_all")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int snd_mixer_selem_set_playback_volume_all(IntPtr elem, long value);

    /// <summary>
    ///     Sets the playback switch (mute/unmute) for all channels of a mixer element.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "snd_mixer_selem_set_playback_switch_all")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int snd_mixer_selem_set_playback_switch_all(IntPtr elem, int val);

    /// <summary>
    ///     Gets the dB playback range for a mixer element.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "snd_mixer_selem_get_playback_dB_range")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int snd_mixer_selem_get_playback_dB_range(
        IntPtr elem,
        out long min,
        out long max
    );

    /// <summary>
    ///     Sets the dB playback value for all channels of a mixer element.
    /// </summary>
    [LibraryImport(LibraryName, EntryPoint = "snd_mixer_selem_set_playback_dB_all")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int snd_mixer_selem_set_playback_dB_all(
        IntPtr elem,
        long value,
        int dir
    );

    /// <summary>
    ///     Returns a human-readable error message for an ALSA error code.
    /// </summary>
    [LibraryImport(LibraryName,
        EntryPoint = "snd_strerror",
        StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial string snd_strerror(int errnum);

    /// <summary>
    ///     Gets a human-readable error message from an ALSA error code.
    /// </summary>
    internal static string GetErrorMessage(int errorCode)
    {
        return snd_strerror(errorCode) ?? $"err{errorCode}";
    }

    /// <summary>
    ///     Throws an InvalidOperationException if the ALSA return code indicates an error.
    /// </summary>
    internal static void CheckResult(int returnCode, string operation)
    {
        if (returnCode < 0)
        {
            throw new InvalidOperationException($"{operation}: {GetErrorMessage(returnCode)}");
        }
    }
}
