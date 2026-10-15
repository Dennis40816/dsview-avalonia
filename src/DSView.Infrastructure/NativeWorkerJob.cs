// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Diagnostics;
using System.Text;

namespace DSView.Infrastructure;

// Owns the generated input and process lifetime together. Input is removed only
// after termination is confirmed; callers never publish partial stdout.
internal static class NativeWorkerJob
{
    public static Task<string> RunAsync(byte[] samples, Func<string, ProcessStartInfo> configure, CancellationToken cancellation) =>
        RunCoreAsync(samples, path => configure(path!), cancellation);
    public static Task<string> RunAsync(ProcessStartInfo start, CancellationToken cancellation) =>
        RunCoreAsync(null, _ => start, cancellation);
    private static async Task<string> RunCoreAsync(byte[]? samples, Func<string?, ProcessStartInfo> configure, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var directory = samples == null ? null : Directory.CreateTempSubdirectory("dsview2-decoder-");
        string? input = directory == null ? null : Path.Combine(directory.FullName, "samples.raw"); bool removable = true; Exception? failure = null;
        try
        {
            if (samples != null) await File.WriteAllBytesAsync(input!, samples, cancellation).ConfigureAwait(false);
            var info = configure(input); info.UseShellExecute = false; info.CreateNoWindow = true;
            info.RedirectStandardOutput = info.RedirectStandardError = true;
            cancellation.ThrowIfCancellationRequested();
            using var process = Process.Start(info) ?? throw new IOException("Could not start native worker.");
            removable = false;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation); deadline.CancelAfter(TimeSpan.FromSeconds(45));
            var stdout = ReadBounded(process.StandardOutput, 4_000_000, deadline);
            var stderr = ReadBounded(process.StandardError, 65_536, deadline);
            try
            {
                await Task.WhenAll(stdout, stderr, process.WaitForExitAsync(deadline.Token)).ConfigureAwait(false);
                cancellation.ThrowIfCancellationRequested();
                if (process.ExitCode != 0) throw new InvalidDataException("Native worker failed. " + (await stderr.ConfigureAwait(false)).Trim());
                return await stdout.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            {
                if (stdout.IsFaulted) await stdout.ConfigureAwait(false);
                if (stderr.IsFaulted) await stderr.ConfigureAwait(false);
                throw new TimeoutException("Native worker exceeded its 45-second limit.");
            }
            finally
            {
                try
                {
                    if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
                    removable = process.HasExited;
                }
                finally
                {
                    deadline.Cancel();
                    try { await stdout.ConfigureAwait(false); } catch { }
                    try { await stderr.ConfigureAwait(false); } catch { }
                    if (!removable) throw new IOException("Native worker termination unconfirmed." + (input == null ? "" : " Generated input retained at " + input));
                }
            }
        }
        catch (Exception ex) { failure = ex; throw; }
        finally
        {
            if (removable && directory != null)
            {
                try { if (File.Exists(input)) File.Delete(input!); directory.Delete(); }
                catch (Exception ex) when (failure != null) { failure.Data["InputCleanupFailure"] = ex.Message; }
            }
        }
    }
    private static async Task<string> ReadBounded(StreamReader reader, int maximum, CancellationTokenSource lifetime)
    {
        var result = new StringBuilder(); var buffer = new char[4096];
        try
        {
            while (true)
            {
                int read = await reader.ReadAsync(buffer, lifetime.Token).ConfigureAwait(false);
                if (read == 0) return result.ToString();
                if (result.Length + read > maximum) throw new InvalidDataException("Native worker output exceeded its limit.");
                result.Append(buffer, 0, read);
            }
        }
        catch { lifetime.Cancel(); throw; }
    }
}
