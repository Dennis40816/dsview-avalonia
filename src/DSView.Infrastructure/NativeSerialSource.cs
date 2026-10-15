// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using DSView.Domain;

namespace DSView.Infrastructure;

public sealed record NativeSerialOptions(string Worker, string WorkerSha256, string DecoderDirectory,
    Dictionary<string, string> DecoderFiles, string PythonHome, string[] RuntimeDirectories);

public static class NativeSerialSource
{
    public static string ConfigurationPath => Path.Combine(AppContext.BaseDirectory, "native-serial.json");
    private static readonly string[] Modules = ["1-spi/pd.py", "1-spi/__init__.py", "1-uart/pd.py", "1-uart/__init__.py",
        "common/__init__.py", "common/srdhelper/__init__.py", "common/srdhelper/mod.py"];
    public static NativeSerialOptions ReadConfiguration(string path)
    {
        using var input = File.OpenRead(path);
        if (input.Length > 16_384) throw new InvalidDataException("Serial runtime configuration is too large.");
        return JsonSerializer.Deserialize<NativeSerialOptions>(input, new JsonSerializerOptions
        { AllowDuplicateProperties = false, RespectRequiredConstructorParameters = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 8 })
            ?? throw new InvalidDataException("Serial runtime configuration is missing.");
    }
    public static Task<SerialDecodeResult> DecodeAsync(Capture raw, DecoderSource source, NativeSerialOptions runtime, CancellationToken cancellation = default) =>
        Task.Run(async () =>
        {
            cancellation.ThrowIfCancellationRequested(); var signals = SerialDecodeRequest.Resolve(raw, source); ValidateRuntime(runtime);
            var samples = I2cSampleInput.PackPlanes(raw, signals, cancellation);
            var json = await NativeWorkerJob.RunAsync(samples, input => Start(runtime, raw, source, input), cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            return source.Spi != null ? SpiEventAdapter.Read(raw, source, json, cancellation) : UartEventAdapter.Read(raw, source, json, cancellation);
        }, cancellation);
    private static ProcessStartInfo Start(NativeSerialOptions runtime, Capture raw, DecoderSource source, string input)
    {
        var info = new ProcessStartInfo(runtime.Worker);
        string N(long value) => value.ToString(CultureInfo.InvariantCulture);
        foreach (string argument in new[] { runtime.DecoderDirectory, source.EffectiveProtocol, input, N(raw.SampleCount), N(raw.SampleRate), N(raw.SampleCount) }) info.ArgumentList.Add(argument);
        string[] arguments = source.Spi is { } spi
            ? [N(spi.Mode / 2), N(spi.Mode % 2), spi.BitOrder, N(spi.WordSize), spi.ChipSelectPolarity, N(SerialDecodeRequest.SpiMask(source))]
            : [N(source.Uart!.BaudRate), N(source.Uart.DataBits), source.Uart.Parity, N(source.Uart.StopBits), source.Uart.BitOrder, source.Uart.Inverted ? "yes" : "no"];
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        info.Environment["PATH"] = string.Join(Path.PathSeparator, runtime.RuntimeDirectories.Concat([info.Environment["PATH"] ?? ""]));
        info.Environment["PYTHONHOME"] = runtime.PythonHome; info.Environment["PYTHONNOUSERSITE"] = "1"; info.Environment["PYTHONDONTWRITEBYTECODE"] = "1"; info.Environment.Remove("PYTHONPATH");
        return info;
    }
    private static void ValidateRuntime(NativeSerialOptions runtime)
    {
        bool DirectoryPath(string? path) => !string.IsNullOrEmpty(path) && Path.IsPathFullyQualified(path) && Directory.Exists(path);
        if (runtime == null || string.IsNullOrEmpty(runtime.Worker) || !Path.IsPathFullyQualified(runtime.Worker)
            || !DirectoryPath(runtime.DecoderDirectory) || !DirectoryPath(runtime.PythonHome) || runtime.RuntimeDirectories == null
            || runtime.RuntimeDirectories.Length > 16 || !runtime.RuntimeDirectories.All(DirectoryPath)
            || runtime.DecoderFiles == null || !runtime.DecoderFiles.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(Modules))
            throw new InvalidDataException("Invalid serial runtime paths or module inventory.");
        CheckHash(runtime.Worker, runtime.WorkerSha256);
        foreach (string module in Modules) CheckHash(Path.Combine(runtime.DecoderDirectory, module), runtime.DecoderFiles[module]);
    }
    private static void CheckHash(string path, string expected)
    {
        using var input = File.OpenRead(path);
        if (expected?.Length != 64 || !Convert.ToHexString(SHA256.HashData(input)).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Serial runtime identity changed. Review its configuration before use.");
    }
}
