// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using DSView.Infrastructure;

internal static class UsbDiscoveryChecks
{
    private static readonly Guid Request = Guid.ParseExact("0123456789abcdef0123456789abcdef", "N");
    private const string Observation = "{\"vendor\":10766,\"product\":32,\"revision\":513,\"bus\":2,\"address\":5,\"speed\":3,\"ports\":[3,1]}";
    private static string Json(string devices = "", bool complete = true, int unreadable = 0) =>
        "{\"schema\":1,\"request\":\"0123456789abcdef0123456789abcdef\",\"scope\":\"usb-descriptors\",\"vendor\":10766,\"complete\":"
        + (complete ? "true" : "false") + ",\"unreadableDescriptors\":" + unreadable + ",\"devices\":[" + devices + "]}";
    public static async Task RunAsync(Action<bool, string> check, string? nativeConfig, string? fixtureLibrary)
    {
        var at = DateTimeOffset.Parse("2026-01-02T03:04:05Z");
        var none = UsbDiscoveryEnvelope.Read(Json(), Request, at);
        var partial = UsbDiscoveryEnvelope.Read(Json(complete: false, unreadable: 1), Request, at);
        check(none.NoMatchingDescriptors && !partial.NoMatchingDescriptors && !partial.Complete && partial.UnreadableDescriptors == 1,
            "Only complete USB discovery can report no matching descriptors; unreadable devices remain explicit uncertainty");
        var found = UsbDiscoveryEnvelope.Read(Json(Observation), Request, at);
        check(found.RequestId == Request && found.ObservedAt == at && !found.NoMatchingDescriptors
            && found.Devices.Single() is { VendorId: 0x2A0E, ProductId: 0x0020, Revision: 0x0201, Bus: 2, Address: 5, Speed: 3 }
            && found.Devices.Single().Ports!.SequenceEqual(new[] { 3, 1 }),
            "USB observations preserve descriptor identity, physical location and snapshot provenance without inventing hardware capabilities");
        var unknown = UsbDiscoveryEnvelope.Read(Json(Observation.Replace("\"product\":32", "\"product\":65535").Replace("[3,1]", "null")), Request, at);
        check(unknown.Devices.Single().ProductId == 65535 && unknown.Devices.Single().Ports == null,
            "An unknown product or unavailable port path remains an observation rather than a guessed DSLogic profile");
        string valid = Json(Observation);
        var bad = new[]
        {
            valid.Replace("\"schema\":1", "\"schema\":2"), valid.Replace("usb-descriptors", "connected"),
            valid.Replace("0123456789abcdef0123456789abcdef", Guid.NewGuid().ToString("N")),
            valid.Replace("\"schema\":1", "\"schema\":1,\"schema\":1"), valid.Replace("\"schema\":1", "\"schema\":1,\"ready\":true"),
            valid.Replace("\"revision\":513,", ""), valid.Replace("\"revision\":513", "\"revision\":-1"),
            valid.Replace("\"address\":5", "\"address\":128"), valid.Replace("\"speed\":3", "\"speed\":7"),
            valid.Replace("[3,1]", "[]"), valid.Replace("[3,1]", "[0]"), valid.Replace("[3,1]", "[1,2,3,4,5,6,7,8]"),
            valid.Replace("\"devices\":[", "\"devices\":null,\"extra\":["), Json("null"), Json(Observation + "," + Observation),
            Json(Observation, false, 0), Json(Observation, true, 1), Json(Observation, false, -1),
            Json(Observation.Replace("10766", "4660")), Json(Observation.Replace("\"bus\":2", "\"bus\":256")),
            Json(string.Join(',', Enumerable.Repeat(Observation, 65))), new string(' ', 65_537)
        };
        int rejected = 0;
        foreach (string json in bad)
        {
            try { UsbDiscoveryEnvelope.Read(json, Request, at); }
            catch (Exception ex) when (ex is InvalidDataException or JsonException) { rejected++; }
        }
        check(rejected == bad.Length, "USB admission rejects stale jobs, unsupported schemas, forged readiness, duplicate locations, malformed fields and contradictory completeness");
        if (nativeConfig == null) { Console.WriteLine("SKIP optional native USB discovery: pass --native-usb <configuration>."); return; }
        var options = NativeUsbDiscovery.ReadConfiguration(nativeConfig);
        if (fixtureLibrary != null) await FixtureAsync(check, options, fixtureLibrary);
        var actual = await NativeUsbDiscovery.DiscoverAsync(options);
        check(actual.RequestId != Guid.Empty && actual.Devices.All(d => d.VendorId == 0x2A0E), "Native descriptor-only USB worker returns a validated real-host snapshot");
        Console.WriteLine($"USB DISCOVERY observedAt={actual.ObservedAt:O}; matching={actual.Devices.Count}; complete={actual.Complete}; unreadable={actual.UnreadableDescriptors}. No device was opened or initialized; this is not connection, acquisition or hardware acceptance.");
    }
    private static async Task FixtureAsync(Action<bool, string> check, NativeUsbDiscoveryOptions options, string library)
    {
        using var input = File.OpenRead(library);
        var testRuntime = options with { Library = Path.GetFullPath(library), LibrarySha256 = Convert.ToHexString(SHA256.HashData(input)) };
        var paired = await NativeUsbDiscovery.DiscoverAsync(testRuntime);
        check(paired.Complete && paired.Devices.Select(d => d.ProductId).SequenceEqual(new[] { 0x20, 0x34 })
            && paired.Devices.Select(d => d.Address).SequenceEqual(new[] { 5, 7 }),
            "Controlled native USB fixture enumerates two distinct devices and filters an unrelated vendor");
        foreach (string mode in new[] { "empty", "partial", "unknown-ports", "init-failure", "list-failure" })
        {
            var start = new ProcessStartInfo(options.Worker);
            start.ArgumentList.Add(Path.GetFullPath(library)); start.ArgumentList.Add(Request.ToString("N"));
            start.Environment["DSVIEW_USB_FIXTURE"] = mode;
            bool correct;
            try
            {
                var json = await NativeWorkerJob.RunAsync(start, CancellationToken.None);
                var result = UsbDiscoveryEnvelope.Read(json, Request, DateTimeOffset.UtcNow);
                correct = mode switch
                {
                    "empty" => result.NoMatchingDescriptors,
                    "partial" => !result.Complete && !result.NoMatchingDescriptors && result.UnreadableDescriptors == 1 && result.Devices.Single().ProductId == 0x34,
                    "unknown-ports" => result.Complete && result.Devices.All(d => d.Ports == null),
                    _ => false
                };
            }
            catch (InvalidDataException ex) when (mode.EndsWith("failure", StringComparison.Ordinal))
            { correct = ex.Message.Contains(mode == "init-failure" ? "initialization failed" : "enumeration failed", StringComparison.Ordinal); }
            check(correct, $"Controlled native USB {mode} does not become a false ready or empty-device success");
        }
        bool changed = false;
        try { await NativeUsbDiscovery.DiscoverAsync(testRuntime with { LibrarySha256 = new string('0', 64) }); }
        catch (InvalidDataException) { changed = true; }
        check(changed, "A changed USB runtime hash is rejected before loading the library");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel(); bool cancelled = false;
        try { await NativeUsbDiscovery.DiscoverAsync(testRuntime, cancellation.Token); } catch (OperationCanceledException) { cancelled = true; }
        check(cancelled, "Cancelled USB discovery does not start a worker or return observations");
    }
}
