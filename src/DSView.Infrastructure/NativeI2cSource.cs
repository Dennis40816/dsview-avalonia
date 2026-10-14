// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DSView.Domain;

namespace DSView.Infrastructure;

public sealed record NativeI2cOptions(string Worker, string WorkerSha256, string DecoderDirectory,
    string DecoderSha256, string InitializerSha256, string PythonHome, string[] RuntimeDirectories);

public static class NativeI2cSource
{
    public static string ConfigurationPath => Path.Combine(AppContext.BaseDirectory, "native-i2c.json");
    public static bool IsConfigured => File.Exists(ConfigurationPath);
    public static NativeI2cOptions ReadConfiguration(string path)
    {
        if (new FileInfo(path).Length > 16_384) throw new InvalidDataException("Decoder configuration is too large.");
        return JsonSerializer.Deserialize<NativeI2cOptions>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Invalid decoder configuration.");
    }
    public static async Task<Capture> LoadAsync(CancellationToken cancellation = default)
    {
        var options = ReadConfiguration(ConfigurationPath);
        var raw = await LoadRawExampleAsync(cancellation).ConfigureAwait(false);
        return await DecodeAsync(raw, options, cancellation).ConfigureAwait(false);
    }
    public static async Task<Capture> LoadRawExampleAsync(CancellationToken cancellation = default) =>
        await DemoCaptureSource.LoadFixtureAsync("i2c-raw", cancellation).ConfigureAwait(false) with { SourceDescription = "Generated raw SCL/SDA. Protocol decoding has not run." };

    public static Task<Capture> DecodeAsync(Capture raw, NativeI2cOptions options, CancellationToken cancellation = default) =>
        Task.Run(() => DecodeCoreAsync(raw, options, cancellation), cancellation);
    private static async Task<Capture> DecodeCoreAsync(Capture raw, NativeI2cOptions options, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        // The worker accepts finite continuous sources only. No gap concatenation.
        if (raw.SampleRate <= 0 || raw.SampleCount < 10 || raw.SampleCount > 64_000_000 || raw.Gaps.Count != 0)
            throw new InvalidDataException("I²C decoding currently requires a finite, continuous capture.");
        ValidateOptions(options);
        var packed = Pack(raw, cancellation);
        var directory = Path.Combine(Path.GetTempPath(), "DSView2", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var input = Path.Combine(directory, "samples.raw");
        bool canRemoveInput = true;
        Exception? failure = null;
        try
        {
            await File.WriteAllBytesAsync(input, packed, cancellation).ConfigureAwait(false);
            var start = new ProcessStartInfo(options.Worker) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { options.DecoderDirectory, input, raw.SampleCount.ToString(CultureInfo.InvariantCulture), raw.SampleRate.ToString(CultureInfo.InvariantCulture), raw.SampleCount.ToString(CultureInfo.InvariantCulture) })
                start.ArgumentList.Add(argument);
            start.Environment["PATH"] = string.Join(Path.PathSeparator, options.RuntimeDirectories.Concat([start.Environment["PATH"] ?? ""]));
            start.Environment["PYTHONHOME"] = options.PythonHome;
            start.Environment["PYTHONNOUSERSITE"] = "1"; start.Environment["PYTHONDONTWRITEBYTECODE"] = "1";
            start.Environment.Remove("PYTHONPATH");
            using var process = Process.Start(start) ?? throw new IOException("Could not start the I²C decoder.");
            canRemoveInput = false;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(45));
            var output = ReadBounded(process.StandardOutput, 4_000_000, timeout);
            var error = ReadBounded(process.StandardError, 65_536, timeout);
            try
            {
                await Task.WhenAll(output, error, process.WaitForExitAsync(timeout.Token)).ConfigureAwait(false);
                cancellation.ThrowIfCancellationRequested();
                if (process.ExitCode != 0) throw new InvalidDataException("I²C decoder failed. " + (await error.ConfigureAwait(false)).Trim());
                var result = I2cEventAdapter.Apply(raw, await output.ConfigureAwait(false));
                cancellation.ThrowIfCancellationRequested();
                return result;
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            { failure = new TimeoutException("I²C decoding did not finish within the configured time limit."); throw failure; }
            catch (Exception ex) { failure = ex; throw; }
            finally
            {
                Exception? teardownFailure = null;
                try
                {
                    if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
                    canRemoveInput = process.HasExited;
                }
                catch (Exception ex) { teardownFailure = ex; }
                finally
                {
                    timeout.Cancel();
                    try { await output.ConfigureAwait(false); } catch { }
                    try { await error.ConfigureAwait(false); } catch { }
                }
                if (teardownFailure != null)
                    throw new AggregateException("Could not confirm decoder termination; generated input was retained at " + input,
                        failure == null ? [teardownFailure] : [failure, teardownFailure]);
            }
        }
        catch (Exception ex) { failure = ex; throw; }
        finally
        {
            // Delete only our input, and only after confirmed worker exit (or before it started).
            if (canRemoveInput)
            {
                try { if (File.Exists(input)) File.Delete(input); Directory.Delete(directory); }
                catch (Exception ex) when (failure != null) { failure.Data["InputCleanupFailure"] = ex.Message; }
            }
        }
    }
    private static async Task<string> ReadBounded(StreamReader reader, int maximum, CancellationTokenSource lifetime)
    {
        var text = new StringBuilder(); var buffer = new char[4096];
        try
        {
            while (true)
            {
                int read = await reader.ReadAsync(buffer, lifetime.Token).ConfigureAwait(false);
                if (read == 0) return text.ToString();
                if (text.Length + read > maximum) throw new InvalidDataException("Decoder output exceeded its limit.");
                text.Append(buffer, 0, read);
            }
        }
        catch { lifetime.Cancel(); throw; }
    }
    private static void ValidateOptions(NativeI2cOptions options)
    {
        if (options == null || options.RuntimeDirectories == null || !Path.IsPathFullyQualified(options.Worker) || !Path.IsPathFullyQualified(options.DecoderDirectory) ||
            !Path.IsPathFullyQualified(options.PythonHome) || !Directory.Exists(options.PythonHome) ||
            options.RuntimeDirectories.Any(path => !Path.IsPathFullyQualified(path) || !Directory.Exists(path)))
            throw new InvalidDataException("Decoder runtime paths must be existing absolute paths.");
        CheckHash(options.Worker, options.WorkerSha256);
        CheckHash(Path.Combine(options.DecoderDirectory, "1-i2c", "pd.py"), options.DecoderSha256);
        CheckHash(Path.Combine(options.DecoderDirectory, "1-i2c", "__init__.py"), options.InitializerSha256);
    }
    private static void CheckHash(string path, string expected)
    {
        using var file = File.OpenRead(path);
        if (expected?.Length != 64 || !Convert.ToHexString(SHA256.HashData(file)).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Decoder runtime identity changed. Review its configuration before using it.");
    }
    private static byte[] Pack(Capture raw, CancellationToken cancellation)
    {
        int size = checked((int)((raw.SampleCount + 7) / 8));
        var bytes = new byte[checked(size * 2)];
        for (int channel = 0; channel < 2; channel++)
        {
            var matches = raw.Signals.Where(s => s.Id == channel).ToArray();
            if (matches.Length != 1) throw new InvalidDataException("I²C requires one D0/SCL and one D1/SDA signal.");
            var signal = matches[0]; long previous = 0; bool high = signal.Initial;
            foreach (var edge in signal.Edges)
            {
                cancellation.ThrowIfCancellationRequested();
                if (edge.Sample <= previous || edge.Sample >= raw.SampleCount || edge.High == high)
                    throw new InvalidDataException("Raw transitions must be ordered, alternating and inside the capture.");
                if (high) Fill(previous, edge.Sample, channel * size);
                previous = edge.Sample; high = edge.High;
            }
            if (high) Fill(previous, raw.SampleCount, channel * size);
        }
        return bytes;
        void Fill(long first, long end, int offset)
        {
            while (first < end && first % 8 != 0) { bytes[offset + (int)(first / 8)] |= (byte)(1 << (int)(first % 8)); first++; }
            long whole = (end - first) / 8;
            Array.Fill(bytes, (byte)255, offset + (int)(first / 8), (int)whole); first += whole * 8;
            while (first < end) { bytes[offset + (int)(first / 8)] |= (byte)(1 << (int)(first % 8)); first++; }
        }
    }
}
