// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using DSView.Domain;

namespace DSView.Infrastructure;

public sealed record NativeUsbDiscoveryOptions(string Worker, string WorkerSha256, string Library, string LibrarySha256);
public static class NativeUsbDiscovery
{
    public static string ConfigurationPath => Path.Combine(AppContext.BaseDirectory, "native-usb.json");
    public static NativeUsbDiscoveryOptions ReadConfiguration(string path)
    {
        using var input = File.OpenRead(path);
        if (input.Length > 16_384) throw new InvalidDataException("USB discovery configuration is too large.");
        return JsonSerializer.Deserialize<NativeUsbDiscoveryOptions>(input, new JsonSerializerOptions
        { AllowDuplicateProperties = false, RespectRequiredConstructorParameters = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 4 })
            ?? throw new InvalidDataException("Missing USB discovery configuration.");
    }
    public static Task<UsbDiscoverySnapshot> DiscoverAsync(NativeUsbDiscoveryOptions runtime, CancellationToken cancellation = default) => Task.Run(async () =>
    {
        cancellation.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("This development USB worker requires Windows.");
        ArgumentNullException.ThrowIfNull(runtime);
        Verify(runtime.Worker, runtime.WorkerSha256); Verify(runtime.Library, runtime.LibrarySha256);
        var request = Guid.NewGuid(); var start = new ProcessStartInfo(runtime.Worker);
        start.ArgumentList.Add(runtime.Library); start.ArgumentList.Add(request.ToString("N"));
        var json = await NativeWorkerJob.RunAsync(start, cancellation).ConfigureAwait(false);
        cancellation.ThrowIfCancellationRequested();
        return UsbDiscoveryEnvelope.Read(json, request, DateTimeOffset.UtcNow);
    }, cancellation);
    private static void Verify(string path, string expected)
    {
        if (string.IsNullOrEmpty(path) || !Path.IsPathFullyQualified(path) || expected?.Length != 64 || !expected.All(char.IsAsciiHexDigit))
            throw new InvalidDataException("USB discovery requires absolute reviewed paths and SHA-256 identities.");
        using var input = File.OpenRead(path);
        if (!Convert.ToHexString(SHA256.HashData(input)).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("USB discovery runtime identity changed.");
    }
}
