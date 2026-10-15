// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DSView.Infrastructure;

public sealed record NativeDslOptions(string Worker, string WorkerSha256, string StagingDirectory, string[] RuntimeDirectories);

public static class NativeDslSource
{
    public static string ConfigurationPath => Path.Combine(AppContext.BaseDirectory, "native-dsl.json");
    public static bool IsConfigured => File.Exists(ConfigurationPath);
    public static NativeDslOptions ReadConfiguration(string path)
    {
        if (new FileInfo(path).Length > 16_384) throw new InvalidDataException("Capture reader configuration is too large.");
        return JsonSerializer.Deserialize<NativeDslOptions>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Invalid capture reader configuration.");
    }
    public static Task<DslImportResult> LoadAsync(string path, NativeDslOptions options, CancellationToken cancellation = default,
        IProgress<string>? progress = null) => Task.Run(() => LoadCoreAsync(path, options, cancellation, progress), cancellation);

    private static async Task<DslImportResult> LoadCoreAsync(string path, NativeDslOptions options, CancellationToken cancellation, IProgress<string>? progress)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows() || options == null || options.RuntimeDirectories == null
            || !Path.IsPathFullyQualified(options.Worker) || !Path.IsPathFullyQualified(options.StagingDirectory)
            || options.StagingDirectory.Length > 180 || options.StagingDirectory.Any(c => c < 32 || c > 126)
            || options.StagingDirectory.Length < 3 || options.StagingDirectory[1] != ':'
            || options.RuntimeDirectories.Any(p => !Path.IsPathFullyQualified(p) || !Directory.Exists(p)))
            throw new InvalidDataException("Capture reader needs existing runtime paths and a writable, short ASCII Windows staging folder.");
        using (var workerFile = File.OpenRead(options.Worker))
            if (options.WorkerSha256?.Length != 64 || !Convert.ToHexString(SHA256.HashData(workerFile)).Equals(options.WorkerSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Capture reader identity changed. Its configuration needs review.");
        Directory.CreateDirectory(options.StagingDirectory);
        string directory = Path.Combine(options.StagingDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string input = Path.Combine(directory, "capture.dsl"), output = Path.Combine(directory, "samples.cross");
        bool safeToClean = true; Exception? failure = null;
        try
        {
            progress?.Report("Copying capture…");
            string digest;
            await using (var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true))
            await using (var staged = new FileStream(input, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                if (source.Length is <= 0 or > AnalysisFile.MaximumBytes) throw new InvalidDataException("Capture file must be between 1 byte and 128 MiB.");
                byte[] buffer = new byte[65536]; long copied = 0; int read;
                while ((read = await source.ReadAsync(buffer, cancellation).ConfigureAwait(false)) != 0)
                {
                    if (read > AnalysisFile.MaximumBytes - copied) throw new InvalidDataException("Capture file exceeds 128 MiB.");
                    await staged.WriteAsync(buffer.AsMemory(0, read), cancellation).ConfigureAwait(false);
                    hash.AppendData(buffer, 0, read); copied += read;
                }
                await staged.FlushAsync(cancellation).ConfigureAwait(false);
                digest = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            }
            // Deny writes and deletion across both native preflight and native reading.
            await using var inputLock = new FileStream(input, FileMode.Open, FileAccess.Read, FileShare.Read);
            cancellation.ThrowIfCancellationRequested();
            var start = new ProcessStartInfo(options.Worker)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false, true), StandardErrorEncoding = new UTF8Encoding(false, true)
            };
            start.ArgumentList.Add(input); start.ArgumentList.Add(output);
            start.Environment["PATH"] = string.Join(Path.PathSeparator, options.RuntimeDirectories.Concat([start.Environment["PATH"] ?? ""]));
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            lifetime.CancelAfter(TimeSpan.FromSeconds(45));
            using var process = Process.Start(start) ?? throw new IOException("Could not start capture reader.");
            safeToClean = false;
            var text = ReadBounded(process.StandardOutput, 32_768, lifetime);
            var error = ReadBounded(process.StandardError, 65_536, lifetime);
            try
            {
                progress?.Report("Reading capture…");
                await Task.WhenAll(text, error, process.WaitForExitAsync(lifetime.Token)).ConfigureAwait(false);
                cancellation.ThrowIfCancellationRequested();
                if (process.ExitCode != 0) throw new InvalidDataException("Capture reader rejected this file. " + (await error.ConfigureAwait(false)).Trim());
                var metadata = DslCrossDataAdapter.ReadMetadata(await text.ConfigureAwait(false));
                progress?.Report("Preparing waveforms…");
                await using var payload = new FileStream(output, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
                return await DslCrossDataAdapter.ReadAsync(payload, metadata, Path.GetFileName(path), digest, cancellation).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            { failure = new TimeoutException("Capture reader did not finish within its time limit."); throw failure; }
            catch (Exception ex) { failure = ex; throw; }
            finally
            {
                try
                {
                    if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
                    safeToClean = process.HasExited;
                }
                catch (Exception ex) { throw new AggregateException("Reader termination could not be confirmed; staging files were retained at " + directory, failure == null ? [ex] : [failure, ex]); }
                finally
                {
                    lifetime.Cancel();
                    try { await text.ConfigureAwait(false); } catch { }
                    try { await error.ConfigureAwait(false); } catch { }
                }
            }
        }
        catch (Exception ex) { failure = ex; throw; }
        finally
        {
            if (safeToClean)
            {
                try { if (File.Exists(output)) File.Delete(output); if (File.Exists(input)) File.Delete(input); Directory.Delete(directory); }
                catch (Exception ex) when (failure != null) { failure.Data["StagingCleanupFailure"] = ex.Message; }
            }
        }
    }
    private static async Task<string> ReadBounded(StreamReader reader, int maximum, CancellationTokenSource lifetime)
    {
        var result = new StringBuilder(); char[] buffer = new char[4096];
        try
        {
            int count;
            while ((count = await reader.ReadAsync(buffer, lifetime.Token).ConfigureAwait(false)) != 0)
            {
                if (count > maximum - result.Length) throw new InvalidDataException("Capture reader output exceeded its limit.");
                result.Append(buffer, 0, count);
            }
            return result.ToString();
        }
        catch { lifetime.Cancel(); throw; }
    }
}
