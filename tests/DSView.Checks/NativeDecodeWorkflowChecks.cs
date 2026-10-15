// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Security.Cryptography;
using System.Text.Json;
using DSView.Checks.Support;
using DSView.Desktop;
using DSView.Domain;
using DSView.Infrastructure;

internal static class NativeDecodeWorkflowChecks
{
    public static async Task RunAsync(Action<bool, string> check, string capturePath, string importConfiguration, string decodeConfiguration)
    {
        var imported = await NativeDslSource.LoadAsync(capturePath, NativeDslSource.ReadConfiguration(importConfiguration));
        var model = new Workbench(); model.RestoreAnalysis(Workbench.CreateImportedAnalysis(imported, [], []));
        var options = NativeI2cSource.ReadConfiguration(decodeConfiguration);
        using var vm = new DecoderViewModel(model, async (c, m, token) => await NativeI2cSource.DecodeMappedAsync(c, new(m.Inputs.Single(i => i.Role == "scl").Channel, m.Inputs.Single(i => i.Role == "sda").Channel), options, token, m.Id));
        vm.Open(); vm.Draft.Role("scl").Selected = vm.Draft.Channels.Single(c => c.Id == 1); vm.Draft.Role("sda").Selected = vm.Draft.Channels.Single(c => c.Id == 0);
        model.ShowAll(); model.Pick(1, 47861, 0);
        var a = model.A; var b = model.B; var raw = model.Capture!;
        await vm.DecodeCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(60));
        var decoded = model.Capture!;
        check(!ReferenceEquals(raw, decoded) && decoded.Packets.Count > 0 && model.A == a && model.B == b
            && decoded.ResolvedDecoders.Single(d => d.Id == "i2c").Channels.SequenceEqual(new[] { 1, 0 })
            && decoded.Signals.SequenceEqual(raw.Signals) && decoded.SourceDescription == raw.SourceDescription,
            "Installed capture imports and decodes through the workflow with D1 SCL / D0 SDA, retaining raw data, provenance and timing");
        var packet = decoded.Packets[0]; model.Select(packet); model.Locate();
        using var workspace = new TestWorkspace(); var path = Path.Combine(workspace.Root, "decoded.dsview2");
        await AnalysisFile.WriteAsync(path, new(AnalysisDocument.FileFormat, AnalysisDocument.CurrentVersion,
            imported.SourceName, decoded, model.SnapshotState(new(1, 0), 1)));
        var reopened = new Workbench(); reopened.RestoreAnalysis(await AnalysisFile.ReadAsync(path));
        check(reopened.Capture!.ResolvedDecoders.Single(d => d.Id == "i2c").Channels.SequenceEqual(new[] { 1, 0 })
            && reopened.A == a && reopened.B == b && reopened.Selected?.Id == packet.Id
            && JsonSerializer.Serialize(reopened.Capture.Packets) == JsonSerializer.Serialize(decoded.Packets)
            && reopened.Group.Channels.Contains(0) && reopened.Group.Channels.Contains(1),
            "Decoded installed capture locates mapped signals and reopens with packet bytes, roles, selection and A/B unchanged");
        var firstSelected = model.Selected;
        vm.Target = vm.Targets[0];
        DecoderDraftChecks.Map(vm.Draft, "scl", 1); DecoderDraftChecks.Map(vm.Draft, "sda", 0);
        await vm.DecodeCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(60));
        check(model.Capture!.Packets.Count == decoded.Packets.Count * 2
            && model.Capture.Packets.Select(p => p.Id).Distinct().Count() == model.Capture.Packets.Count
            && model.Capture.Packets.Count(p => p.Decoder == "i2c-1") == decoded.Packets.Count
            && ReferenceEquals(firstSelected, model.Selected) && model.A == a && model.B == b,
            "A second installed-capture I2C instance has unique records and preserves the selected original instance and timing");
        using var input = File.OpenRead(capturePath);
        Console.WriteLine($"NATIVE WORKFLOW source={capturePath}; sha256={Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant()}; rate={raw.SampleRate}; samples={raw.SampleCount}; packets={decoded.Packets.Count}; first={packet.Address}/{Convert.ToHexString(packet.Bytes)}; A={a?.Sample}; B={b?.Sample}. No native UI or hardware acceptance.");
    }
}
