// DoorbellService.cs
// .NET 9 / C# 13 — single class w/ background logger that falls back to user-writable log dir.
// Changes:
// - ALSA DllImport now targets "libasound.so.2" (soname).
// - PCM fallback is computed from DOORBELL_CARD at runtime (hw:<cardIndex>,0).

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace DoorbellSvc;

public sealed class DoorbellService
{
    private const int DefaultVolume = 80; // 0..200
    private const double MaxGainDb = 6.0; // clamp softvol gain
    private const uint SampleRate = 48000;
    private const uint Channels = 2; // stereo
    private const uint TargetLatencyUs = 50_000; // 50ms

    // Safety caps
    private const int MaxJsonBytes = 64 * 1024;
    private const int MaxRepeat = 50;
    private const int MaxDelayMs = 60_000;
    private const int MaxWavBuffer = 192_000; // bytes
    private const long MaxWavSize = 25L * 1024 * 1024;

    // ===== Static config (device/card may be set via env) =====
    private static readonly string SoundsDir = "/var/lib/doorbell/sounds";
    private static readonly string CacheDir = "/var/lib/doorbell/cache";
    private static readonly string DefaultPcmName = "doorbell_out"; // ALSA softvol PCM
    private static readonly string SoftvolCtrlName = "Doorbell Gain";

    // Socket path stays under /run (systemd). If running as a user without /run perms,
    // you can point Home Assistant to an alternate location; keeping this unchanged here.
    private static readonly string SockPath = "/run/doorbell.sock";

    // Preferred system log dir (will auto-fallback if not writable):
    private static readonly string PreferredLogDir = "/var/log/doorbelld";
    private static readonly string LogFileName = "doorbelld.log";
    private readonly int _cardIndex;

    // Derived from env
    private readonly string _pcmName;

    // Non-overlap guard
    private readonly object _playLock = new();
    private bool _busy;
    private Mixer? _mixer;

    // Core objs
    private PcmPlayer? _pcm;

    public DoorbellService()
    {
        var envDev = Environment.GetEnvironmentVariable("DOORBELL_DEVICE");
        _pcmName = string.IsNullOrWhiteSpace(envDev) ? DefaultPcmName : envDev;

        _cardIndex = int.TryParse(Environment.GetEnvironmentVariable("DOORBELL_CARD"), out var ci) ? ci : 1;
    }

    public void Run()
    {
        Directory.CreateDirectory("/run");

        // Pick a writable log path (system → user fallbacks)
        var logPath = ResolveWritableLogPath();
        Logger.Init(logPath);

        try
        {
            if (File.Exists(SockPath))
            {
                File.Delete(SockPath);
            }

            _pcm = new PcmPlayer(_pcmName, _cardIndex);
            _mixer = new Mixer(_cardIndex);

            var ep = new UnixDomainSocketEndPoint(SockPath);
            using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            listener.Bind(ep);
            // rw for user+group
            try
            {
                File.SetUnixFileMode(SockPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.GroupWrite);
            }
            catch
            {
                /* ignore if FS doesn't support */
            }

            listener.Listen(16);

            Logger.Info($"daemon ready on {SockPath}; PCM={_pcm.DeviceName} card={_cardIndex}");
            Logger.Info($"logging to: {logPath}");

            while (true)
            {
                using var sock = listener.Accept();
                HandleClient(sock);
            }
        }
        catch (Exception ex)
        {
            Logger.Info($"fatal: {ex.Message}");
            throw;
        }
        finally
        {
            Logger.Stop();
            _mixer?.Dispose();
            _pcm?.Dispose();
        }
    }

    // ===== Writable log path resolution =====
    private static string ResolveWritableLogPath()
    {
        // ordered candidates (dir, mustExist? false)
        var uid = GetUid();
        var exeDir = AppContext.BaseDirectory?.TrimEnd('/') ?? "/tmp";
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var xdgState = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
        var xdgCache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");

        var candidates = new List<string>
        {
            // 1) /var/log/doorbelld
            Path.Combine(PreferredLogDir, LogFileName)
        };

        // 2) $XDG_STATE_HOME/doorbelld
        if (!string.IsNullOrWhiteSpace(xdgState))
        {
            candidates.Add(Path.Combine(xdgState!, "doorbelld", LogFileName));
        }

        // 3) $HOME/.local/state/doorbelld
        if (!string.IsNullOrWhiteSpace(home))
        {
            candidates.Add(Path.Combine(home!, ".local", "state", "doorbelld", LogFileName));
        }

        // 4) $XDG_CACHE_HOME/doorbelld
        if (!string.IsNullOrWhiteSpace(xdgCache))
        {
            candidates.Add(Path.Combine(xdgCache!, "doorbelld", LogFileName));
        }

        // 5) <exeDir>/logs/doorbelld.log
        candidates.Add(Path.Combine(exeDir, "logs", LogFileName));

        // 6) /tmp/doorbelld-<uid>/doorbelld.log
        candidates.Add(Path.Combine("/tmp", $"doorbelld-{uid}", LogFileName));

        foreach (var path in candidates)
        {
            try
            {
                var dir = Path.GetDirectoryName(path)!;
                Directory.CreateDirectory(dir);
                using (var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
                {
                    // quick write test
                    var probe = Encoding.UTF8.GetBytes($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] ==== log probe ====\n");
                    fs.Write(probe, 0, probe.Length);
                }

                return path;
            }
            catch
            {
                // try next
            }
        }

        // last resort — current dir
        var fallback = Path.Combine(exeDir, LogFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(fallback)!);
        return fallback;
    }

    private static int GetUid()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return (int) geteuid();
            }
        }
        catch
        {
        }

        return unchecked(Environment.UserName.GetHashCode());
    }

    [DllImport("c")]
    private static extern uint geteuid();

    // ===== Client handling =====
    private void HandleClient(Socket sock)
    {
        try
        {
            var ms = new MemoryStream(1024);
            var buf = new byte[2048];
            var total = 0;
            sock.ReceiveTimeout = 2000;
            while (true)
            {
                int n;
                try
                {
                    n = sock.Receive(buf);
                }
                catch (SocketException)
                {
                    break;
                }

                if (n <= 0)
                {
                    break;
                }

                ms.Write(buf, 0, n);
                total += n;
                if (total > MaxJsonBytes)
                {
                    SendJson(sock, new {ok = false, error = "request too large"});
                    return;
                }

                if (sock.Available == 0)
                {
                    break;
                }
            }

            var span = ms.GetBuffer().AsSpan(0, (int) ms.Length);
            Msg? msg;
            try
            {
                msg = JsonSerializer.Deserialize<Msg>(span);
            }
            catch (Exception ex)
            {
                SendJson(sock, new {ok = false, error = "bad json", detail = ex.Message});
                return;
            }

            if (msg is null)
            {
                SendJson(sock, new {ok = false, error = "empty msg"});
                return;
            }

            switch (msg.cmd)
            {
                case "play": HandlePlay(sock, msg); break;
                case "prewarm": HandlePrewarm(sock, msg); break;
                case "ping": SendJson(sock, new {ok = true, pong = 1}); break;
                default: SendJson(sock, new {ok = false, error = "unknown cmd"}); break;
            }
        }
        catch (Exception ex)
        {
            Logger.Info($"handler error: {ex.Message}");
        }
    }

    private void HandlePlay(Socket sock, Msg msg)
    {
        if (_pcm is null || _mixer is null)
        {
            SendJson(sock, new {ok = false, error = "audio not initialized"});
            return;
        }

        var vol = ClampInt(msg.volume, 0, 200, DefaultVolume);
        var repeat = ClampInt(msg.repeat, 1, MaxRepeat, 1);
        var delay = ClampInt(msg.delay_ms, 0, MaxDelayMs, 0);

        var fileOnly = Path.GetFileName(msg.file ?? "");
        if (string.IsNullOrWhiteSpace(fileOnly) || !IsSafeFileName(fileOnly))
        {
            SendJson(sock, new {ok = false, error = "invalid file"});
            return;
        }

        var shouldPlay = true;
        lock (_playLock)
        {
            if (_busy && msg.queue == 0)
            {
                shouldPlay = false;
            }
            else if (!_busy)
            {
                _busy = true;
            }
        }

        if (!shouldPlay)
        {
            SendJson(sock, new {ok = false, reason = "busy"});
            return;
        }

        try
        {
            Play(_pcm, _mixer, fileOnly, vol, repeat, delay);
            SendJson(sock, new {ok = true});
        }
        catch (Exception ex)
        {
            Logger.Info($"Play error: {ex.Message}");
            SendJson(sock, new {ok = false, error = ex.Message});
        }
        finally
        {
            lock (_playLock)
            {
                _busy = false;
            }
        }
    }

    private void HandlePrewarm(Socket sock, Msg msg)
    {
        var (tot, built, up, failed) = Prewarm(msg.ffmpeg != 0);
        SendJson(sock, new {ok = true, total = tot, built, up_to_date = up, failed});
    }

    // ===== Core features =====
    private static (int mixPercent, double softvolDb) MapVolume(int v)
    {
        v = Math.Clamp(v, 0, 200);
        if (v <= 100)
        {
            return (v, 0.0);
        }

        var db = 20.0 * Math.Log10(v / 100.0);
        if (db > MaxGainDb)
        {
            db = MaxGainDb;
        }

        return (100, db);
    }

    private static (bool ok, int dataOffset, int dataLength) ReadWavHeader(Stream s)
    {
        if (!s.CanRead || !s.CanSeek)
        {
            return (false, 0, 0);
        }

        if (s.Length < 44)
        {
            return (false, 0, 0);
        }

        Span<byte> hdr = stackalloc byte[44];
        s.Position = 0;
        if (s.Read(hdr) != 44)
        {
            return (false, 0, 0);
        }

        if (Encoding.ASCII.GetString(hdr[..4]) != "RIFF")
        {
            return (false, 0, 0);
        }

        if (Encoding.ASCII.GetString(hdr.Slice(8, 4)) != "WAVE")
        {
            return (false, 0, 0);
        }

        long pos = 12;
        while (pos + 8 <= s.Length)
        {
            s.Position = pos;
            Span<byte> ch = stackalloc byte[8];
            if (s.Read(ch) != 8)
            {
                break;
            }

            var id = Encoding.ASCII.GetString(ch[..4]);
            var size = BitConverter.ToInt32(ch.Slice(4, 4)); // little endian

            var dataStart = pos + 8;
            var dataEnd = dataStart + (uint) size;
            if (dataEnd > s.Length)
            {
                return (false, 0, 0);
            }

            if (id == "data")
            {
                if (s.Length > MaxWavSize)
                {
                    return (false, 0, 0);
                }

                return (true, (int) dataStart, size);
            }

            pos = dataEnd;
        }

        return (false, 0, 0);
    }

    private static void SendJson(Socket sock, object obj)
    {
        try
        {
            var json = JsonSerializer.Serialize(obj);
            sock.Send(Encoding.UTF8.GetBytes(json));
        }
        catch
        {
            /* ignore */
        }
    }

    private static bool HasCmd(string exe)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        foreach (var p in path.Split(':'))
        {
            if (File.Exists(Path.Combine(p, exe)))
            {
                return true;
            }
        }

        return false;
    }

    private static int ClampInt(int value, int min, int max, int fallback)
    {
        return value < min ? fallback : value > max ? max : value;
    }

    private static bool IsSafeFileName(string name)
    {
        if (name.Length == 0 || name.Length > 64)
        {
            return false;
        }

        foreach (var ch in name)
        {
            if (!(char.IsLetterOrDigit(ch) || ch is '.' or '_' or '-'))
            {
                return false;
            }
        }

        return true;
    }

    private void Play(PcmPlayer pcm, Mixer mixer, string baseFile, int volume, int repeat, int delayMs)
    {
        var src = Path.Combine(SoundsDir, baseFile);
        if (!File.Exists(src))
        {
            throw new FileNotFoundException($"Sound not found: {baseFile}", src);
        }

        var play = src;
        if (!baseFile.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
        {
            var cached = Path.Combine(CacheDir, Path.GetFileNameWithoutExtension(baseFile) + ".wav");
            if (File.Exists(cached))
            {
                play = cached;
            }
        }

        var (mixPercent, softDb) = MapVolume(volume);
        mixer.SetPlaybackDb(SoftvolCtrlName, softDb);
        mixer.SetPlaybackPercent("Master", mixPercent);
        mixer.SetPlaybackPercent("Speaker", mixPercent);

        using var fs = File.OpenRead(play);
        if (fs.Length > MaxWavSize)
        {
            throw new InvalidOperationException("WAV too large");
        }

        var (ok, dataOffset, dataLen) = ReadWavHeader(fs);
        if (!ok)
        {
            throw new InvalidOperationException("Unsupported/corrupt WAV (expect PCM S16LE/48k/stereo)");
        }

        if ((long) dataOffset + dataLen > fs.Length)
        {
            throw new InvalidOperationException("WAV data chunk exceeds file length");
        }

        var buf = new byte[Math.Min(MaxWavBuffer, dataLen)];
        for (var r = 0; r < Math.Max(1, repeat); r++)
        {
            fs.Position = dataOffset;
            var left = dataLen;
            while (left > 0)
            {
                var toRead = Math.Min(left, buf.Length);
                var got = fs.Read(buf, 0, toRead);
                if (got <= 0)
                {
                    break;
                }

                pcm.WriteFrames(new ReadOnlySpan<byte>(buf, 0, got));
                left -= got;
            }

            if (r + 1 < repeat && delayMs > 0)
            {
                Thread.Sleep(delayMs);
            }
        }

        mixer.MutePlayback("Speaker");
        mixer.MutePlayback("Master");

        var volTxt = softDb > 0 ? $"{volume}% (mixer {mixPercent}% + softvol +{softDb:F2} dB)" : $"{volume}%";
        Logger.Info($"Played sound {Path.GetFileName(play)} at {volTxt}, repeat={repeat}, delay={delayMs}ms, device={pcm.DeviceName}");
    }

    private (int total, int built, int upToDate, int failed) Prewarm(bool runFfmpeg)
    {
        Directory.CreateDirectory(CacheDir);
        int total = 0, built = 0, up = 0, failed = 0;
        var ffmpegOk = runFfmpeg && HasCmd("ffmpeg");

        foreach (var f in Directory.EnumerateFiles(SoundsDir))
        {
            var ext = Path.GetExtension(f).ToLowerInvariant();
            if (ext == ".wav")
            {
                continue;
            }

            total++;

            var baseNoExt = Path.GetFileNameWithoutExtension(f);
            var cached = Path.Combine(CacheDir, baseNoExt + ".wav");

            try
            {
                var needs = !File.Exists(cached) || File.GetLastWriteTimeUtc(f) > File.GetLastWriteTimeUtc(cached);
                if (!needs)
                {
                    Logger.Info($"Prewarm: up-to-date {Path.GetFileName(f)}");
                    up++;
                    continue;
                }

                if (!ffmpegOk)
                {
                    Logger.Info($"Prewarm: would build {Path.GetFileName(f)} -> {cached} (ffmpeg unavailable/disabled)");
                    failed++;
                    continue;
                }

                var psi = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    ArgumentList = {"-nostdin", "-v", "error", "-y", "-i", f, "-vn", "-acodec", "pcm_s16le", "-ar", "48000", "-ac", "2", "-f", "wav", cached},
                    UseShellExecute = false,
                    RedirectStandardError = false,
                    RedirectStandardOutput = false
                };
                using var p = Process.Start(psi)!;
                p.WaitForExit();
                if (p.ExitCode == 0)
                {
                    Logger.Info($"Prewarm: built {Path.GetFileName(f)} -> {cached} (ok)");
                    built++;
                }
                else
                {
                    Logger.Info($"Prewarm: FAILED {Path.GetFileName(f)} -> {cached} (ffmpeg rc={p.ExitCode})");
                    failed++;
                }
            }
            catch (Exception ex)
            {
                Logger.Info($"Prewarm: ERROR {Path.GetFileName(f)} -> {cached}: {ex.Message}");
                failed++;
            }
        }

        Logger.Info($"Prewarm: summary total={total} built={built} up_to_date={up} failed={failed}");
        return (total, built, up, failed);
    }

    // ===== Nested helpers =====
    private static class Logger
    {
        private static readonly BlockingCollection<string> Q =
            new(new ConcurrentQueue<string>(), 8192);

        private static Thread? _th;
        private static FileStream? _fs;
        private static StreamWriter? _sw;

        public static void Init(string path)
        {
            var dir = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(dir);
            _fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read,
                262_144, FileOptions.WriteThrough);
            _sw = new StreamWriter(_fs, new UTF8Encoding(false)) {AutoFlush = false};
            _th = new Thread(Run) {IsBackground = true, Name = "doorbelld-logger"};
            _th.Start();
            Info("==== doorbelld started ====");
        }

        public static void Info(string msg)
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {msg}";
            if (!Q.TryAdd(line))
            {
                Q.Add(line);
            }
        }

        private static void Run()
        {
            try
            {
                foreach (var line in Q.GetConsumingEnumerable())
                {
                    _sw!.WriteLine(line);
                    _sw.Flush();
                }
            }
            catch
            {
                /* ignore */
            }
            finally
            {
                try
                {
                    _sw?.Flush();
                }
                catch
                {
                }

                try
                {
                    _sw?.Dispose();
                }
                catch
                {
                }

                try
                {
                    _fs?.Dispose();
                }
                catch
                {
                }
            }
        }

        public static void Stop()
        {
            try
            {
                Q.CompleteAdding();
            }
            catch
            {
            }

            try
            {
                _th?.Join(1000);
            }
            catch
            {
            }
        }
    }

    private static class Alsa
    {
        private const string Lib = "libasound.so.2";
        public const int SND_PCM_STREAM_PLAYBACK = 0;
        public const int SND_PCM_FORMAT_S16_LE = 2;
        public const int SND_PCM_ACCESS_RW_INTERLEAVED = 3;

        [DllImport(Lib)]
        public static extern int snd_pcm_open(out IntPtr pcm, string name, int stream, int mode);

        [DllImport(Lib)]
        public static extern int snd_pcm_close(IntPtr pcm);

        [DllImport(Lib)]
        public static extern int snd_pcm_set_params(IntPtr pcm,
            int format,
            int access,
            uint rate,
            uint channels,
            int soft_resample,
            uint latency_us
        );

        [DllImport(Lib)]
        public static extern int snd_pcm_prepare(IntPtr pcm);

        [DllImport(Lib)]
        public static extern long snd_pcm_writei(IntPtr pcm, IntPtr buffer, ulong frames);

        [DllImport(Lib)]
        public static extern int snd_pcm_drain(IntPtr pcm);

        [DllImport(Lib)]
        public static extern IntPtr snd_strerror(int errnum);

        [DllImport(Lib)]
        public static extern int snd_mixer_open(out IntPtr handle, int mode);

        [DllImport(Lib)]
        public static extern int snd_mixer_close(IntPtr handle);

        [DllImport(Lib)]
        public static extern int snd_mixer_attach(IntPtr handle, string name);

        [DllImport(Lib)]
        public static extern int snd_mixer_selem_register(IntPtr handle, IntPtr options, IntPtr classp);

        [DllImport(Lib)]
        public static extern int snd_mixer_load(IntPtr handle);

        [DllImport(Lib)]
        public static extern int snd_mixer_selem_id_malloc(out IntPtr ptr);

        [DllImport(Lib)]
        public static extern void snd_mixer_selem_id_free(IntPtr ptr);

        [DllImport(Lib)]
        public static extern void snd_mixer_selem_id_set_name(IntPtr obj, string name);

        [DllImport(Lib)]
        public static extern void snd_mixer_selem_id_set_index(IntPtr obj, uint index);

        [DllImport(Lib)]
        public static extern IntPtr snd_mixer_find_selem(IntPtr handle, IntPtr id);

        [DllImport(Lib)]
        public static extern int snd_mixer_selem_get_playback_volume_range(IntPtr elem, out long min, out long max);

        [DllImport(Lib)]
        public static extern int snd_mixer_selem_set_playback_volume_all(IntPtr elem, long value);

        [DllImport(Lib)]
        public static extern int snd_mixer_selem_set_playback_switch_all(IntPtr elem, int val);

        [DllImport(Lib)]
        public static extern int snd_mixer_selem_get_playback_dB_range(IntPtr elem, out long min, out long max);

        [DllImport(Lib)]
        public static extern int snd_mixer_selem_set_playback_dB_all(IntPtr elem, long value, int dir);

        public static string Err(int rc)
        {
            return Marshal.PtrToStringAnsi(snd_strerror(rc)) ?? $"err{rc}";
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct snd_mixer_selem_id_t
        {
            private IntPtr p;
        }
    }

    private sealed class Mixer : IDisposable
    {
        private readonly int _card;
        private readonly IntPtr _h;

        public Mixer(int card)
        {
            _card = card;
            var rc = Alsa.snd_mixer_open(out _h, 0);
            Check(rc, "mixer_open");
            rc = Alsa.snd_mixer_attach(_h, $"hw:{_card}");
            Check(rc, "mixer_attach");
            rc = Alsa.snd_mixer_selem_register(_h, IntPtr.Zero, IntPtr.Zero);
            Check(rc, "mixer_register");
            rc = Alsa.snd_mixer_load(_h);
            Check(rc, "mixer_load");
        }

        public void Dispose()
        {
            if (_h != IntPtr.Zero)
            {
                _ = Alsa.snd_mixer_close(_h);
            }
        }

        private static void Check(int rc, string what)
        {
            if (rc < 0)
            {
                throw new InvalidOperationException($"{what}: {Alsa.Err(rc)}");
            }
        }

        private IntPtr FindElem(string name)
        {
            var ok = Alsa.snd_mixer_selem_id_malloc(out var id);
            if (ok < 0)
            {
                return IntPtr.Zero;
            }

            try
            {
                Alsa.snd_mixer_selem_id_set_name(id, name);
                Alsa.snd_mixer_selem_id_set_index(id, 0);
                return Alsa.snd_mixer_find_selem(_h, id);
            }
            finally
            {
                Alsa.snd_mixer_selem_id_free(id);
            }
        }

        public void SetPlaybackPercent(string ctrlName, int percent, bool unmute = true)
        {
            var elem = FindElem(ctrlName);
            if (elem == IntPtr.Zero)
            {
                return;
            }

            if (Alsa.snd_mixer_selem_get_playback_volume_range(elem, out var min, out var max) == 0)
            {
                percent = Math.Clamp(percent, 0, 100);
                var val = min + (long) ((max - min) * (percent / 100.0));
                _ = Alsa.snd_mixer_selem_set_playback_volume_all(elem, val);
            }

            if (unmute)
            {
                _ = Alsa.snd_mixer_selem_set_playback_switch_all(elem, 1);
            }
        }

        public void MutePlayback(string ctrlName)
        {
            var elem = FindElem(ctrlName);
            if (elem == IntPtr.Zero)
            {
                return;
            }

            _ = Alsa.snd_mixer_selem_set_playback_switch_all(elem, 0);
            if (Alsa.snd_mixer_selem_get_playback_volume_range(elem, out var min, out _) == 0)
            {
                _ = Alsa.snd_mixer_selem_set_playback_volume_all(elem, min);
            }
        }

        public void SetPlaybackDb(string ctrlName, double db)
        {
            var elem = FindElem(ctrlName);
            if (elem == IntPtr.Zero)
            {
                return;
            }

            if (Alsa.snd_mixer_selem_get_playback_dB_range(elem, out var minDb, out var maxDb) == 0)
            {
                var hundredths = (long) Math.Round(db * 100.0);
                hundredths = Math.Clamp(hundredths, minDb, maxDb);
                _ = Alsa.snd_mixer_selem_set_playback_dB_all(elem, hundredths, 0);
            }
        }
    }

    private sealed class PcmPlayer : IDisposable
    {
        private IntPtr _pcm = IntPtr.Zero;

        public PcmPlayer(string preferred, int cardIndex)
        {
            var name = preferred;
            var rc = Alsa.snd_pcm_open(out _pcm, name, Alsa.SND_PCM_STREAM_PLAYBACK, 0);
            if (rc < 0)
            {
                // Dynamic fallback based on DOORBELL_CARD
                name = $"hw:{cardIndex},0";
                rc = Alsa.snd_pcm_open(out _pcm, name, Alsa.SND_PCM_STREAM_PLAYBACK, 0);
            }

            if (rc < 0)
            {
                throw new InvalidOperationException($"snd_pcm_open: {Alsa.Err(rc)}");
            }

            rc = Alsa.snd_pcm_set_params(_pcm,
                Alsa.SND_PCM_FORMAT_S16_LE,
                Alsa.SND_PCM_ACCESS_RW_INTERLEAVED,
                SampleRate, Channels, 1, TargetLatencyUs);
            if (rc < 0)
            {
                throw new InvalidOperationException($"snd_pcm_set_params: {Alsa.Err(rc)}");
            }

            DeviceName = name;
        }

        public string DeviceName { get; }

        public void Dispose()
        {
            if (_pcm != IntPtr.Zero)
            {
                _ = Alsa.snd_pcm_drain(_pcm);
                _ = Alsa.snd_pcm_close(_pcm);
                _pcm = IntPtr.Zero;
            }
        }

        public void WriteFrames(ReadOnlySpan<byte> interleavedS16)
        {
            var frames = (ulong) (interleavedS16.Length / (Channels * 2));
            if (frames == 0)
            {
                return;
            }

            unsafe
            {
                fixed (byte* p = interleavedS16)
                {
                    var rc = Alsa.snd_pcm_writei(_pcm, (IntPtr) p, frames);
                    if (rc < 0)
                    {
                        _ = Alsa.snd_pcm_prepare(_pcm);
                        _ = Alsa.snd_pcm_writei(_pcm, (IntPtr) p, frames);
                    }
                }
            }
        }
    }

    // ===== Message DTO =====
    private sealed class Msg
    {
        public string cmd { get; set; } = ""; // "play" | "ping" | "prewarm"
        public string file { get; set; } = "";
        public int volume { get; set; } = DefaultVolume; // 0..200
        public int repeat { get; set; } = 1;
        public int delay_ms { get; set; }
        public int queue { get; set; } // 1=wait if busy; 0=skip
        public int ffmpeg { get; set; } = 1; // prewarm: 1 convert, 0 scan-only
    }
}
