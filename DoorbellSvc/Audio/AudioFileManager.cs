using System.Diagnostics;
using System.Text;
using DoorbellSvc.Configuration;
using DoorbellSvc.Logging;

namespace DoorbellSvc.Audio;

/// <summary>
///     Manages audio file operations including WAV validation and caching
/// </summary>
public static class AudioFileManager
{
    /// <summary>
    ///     Validates and reads WAV file header
    /// </summary>
    public static (bool isValid, int dataOffset, int dataLength) ReadWavHeader(Stream stream)
    {
        if (!stream.CanRead || !stream.CanSeek || stream.Length < 44)
        {
            return (false, 0, 0);
        }

        Span<byte> header = stackalloc byte[44];
        stream.Position = 0;
        if (stream.Read(header) != 44)
        {
            return (false, 0, 0);
        }

        // Validate RIFF header
        if (Encoding.ASCII.GetString(header[..4]) != "RIFF" ||
            Encoding.ASCII.GetString(header.Slice(8, 4)) != "WAVE")
        {
            return (false, 0, 0);
        }

        // Find data chunk
        var position = 12L;
        while (position + 8 <= stream.Length)
        {
            stream.Position = position;
            Span<byte> chunkHeader = stackalloc byte[8];
            if (stream.Read(chunkHeader) != 8)
            {
                break;
            }

            var chunkId = Encoding.ASCII.GetString(chunkHeader[..4]);
            var chunkSize = BitConverter.ToInt32(chunkHeader.Slice(4, 4));

            var dataStart = position + 8;
            var dataEnd = dataStart + (uint) chunkSize;

            if (dataEnd > stream.Length)
            {
                return (false, 0, 0);
            }

            if (chunkId == "data")
            {
                if (stream.Length > DoorbellConfiguration.MaxWavSize)
                {
                    return (false, 0, 0);
                }

                return (true, (int) dataStart, chunkSize);
            }

            position = dataEnd;
        }

        return (false, 0, 0);
    }

    /// <summary>
    ///     Validate if filename is safe for use
    /// </summary>
    public static bool IsSafeFileName(string fileName)
    {
        if (string.IsNullOrEmpty(fileName) || fileName.Length > 64)
        {
            return false;
        }

        return fileName.All(ch => char.IsLetterOrDigit(ch) || ch is '.' or '_' or '-');
    }

    /// <summary>
    ///     Check if a command exists in PATH
    /// </summary>
    public static bool HasCommand(string executable)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        return path.Split(':')
            .Any(directory => File.Exists(Path.Combine(directory, executable)));
    }

    /// <summary>
    ///     Preprocess audio files in the sounds directory
    /// </summary>
    public static (int total, int built, int upToDate, int failed) PrewarmCache(
        string soundsDirectory,
        string cacheDirectory,
        bool enableFfmpeg
    )
    {
        Directory.CreateDirectory(cacheDirectory);

        int total = 0, built = 0, upToDate = 0, failed = 0;
        var ffmpegAvailable = enableFfmpeg && HasCommand("ffmpeg");

        foreach (var filePath in Directory.EnumerateFiles(soundsDirectory))
        {
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            if (extension == ".wav")
            {
                continue; // Skip WAV files
            }

            total++;

            var baseNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
            var cachedPath = Path.Combine(cacheDirectory, baseNameWithoutExtension + ".wav");

            try
            {
                var needsConversion = !File.Exists(cachedPath) ||
                                      File.GetLastWriteTimeUtc(filePath) > File.GetLastWriteTimeUtc(cachedPath);

                if (!needsConversion)
                {
                    BackgroundLogger.Info($"Prewarm: up-to-date {Path.GetFileName(filePath)}");
                    upToDate++;
                    continue;
                }

                if (!ffmpegAvailable)
                {
                    BackgroundLogger.Info($"Prewarm: would build {Path.GetFileName(filePath)} -> {cachedPath} (ffmpeg unavailable/disabled)");
                    failed++;
                    continue;
                }

                if (ConvertWithFfmpeg(filePath, cachedPath))
                {
                    BackgroundLogger.Info($"Prewarm: built {Path.GetFileName(filePath)} -> {cachedPath} (ok)");
                    built++;
                }
                else
                {
                    BackgroundLogger.Info($"Prewarm: FAILED {Path.GetFileName(filePath)} -> {cachedPath}");
                    failed++;
                }
            }
            catch (Exception ex)
            {
                BackgroundLogger.Info($"Prewarm: ERROR {Path.GetFileName(filePath)} -> {cachedPath}: {ex.Message}");
                failed++;
            }
        }

        BackgroundLogger.Info($"Prewarm: summary total={total} built={built} up_to_date={upToDate} failed={failed}");
        return (total, built, upToDate, failed);
    }

    private static bool ConvertWithFfmpeg(string inputPath, string outputPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            ArgumentList =
            {
                "-nostdin", "-v", "error", "-y", "-i", inputPath,
                "-vn", "-acodec", "pcm_s16le", "-ar", "48000", "-ac", "2",
                "-f", "wav", outputPath
            },
            UseShellExecute = false,
            RedirectStandardError = false,
            RedirectStandardOutput = false
        };

        using var process = Process.Start(startInfo);
        if (process == null)
        {
            return false;
        }

        process.WaitForExit();
        return process.ExitCode == 0;
    }
}