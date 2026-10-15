// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Json;
using DSView.Checks.Support;
using DSView.Desktop;
using DSView.Domain;
using DSView.Infrastructure;

internal static class NativeSerialWorkflowChecks
{
    public static async Task RunAsync(Action<bool, string> check, string configuration)
    {
        var runtime = NativeSerialSource.ReadConfiguration(configuration);
        foreach (bool spi in new[] { true, false })
        {
            var (raw, expected) = spi ? NativeSerialChecks.SpiStimulus() : NativeSerialChecks.UartStimulus();
            var model = new Workbench(); model.Load(raw); model.ShowAll(); model.SetMode(2);
            int channel = spi ? 19 : 23; model.Pick(channel, 30, 0); model.Pick(channel, 70, 0); model.SetRange(5, 100);
            var context = (model.Group, model.A, model.B, model.Start, model.End);
            using var vm = new DecoderViewModel(model, async (c, s, token) => await NativeSerialSource.DecodeAsync(c, s, runtime, token));
            vm.Open(); Configure(vm.Draft, expected);
            await vm.DecodeCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(60));
            var first = model.Capture!; string id = spi ? "spi" : "uart";
            check(!ReferenceEquals(first, raw) && first.Packets.Count == 1 && first.Packets.Single().SerialWords.First().Word.Hex == "1A5"
                && first.DecoderSources!.Single().SameConfiguration(expected with { Id = id })
                && context == (model.Group, model.A, model.B, model.Start, model.End) && first.Signals.SequenceEqual(raw.Signals),
                $"Native {(spi ? "SPI" : "UART")} settings run through the shared workflow, preserving physical signals, Group, time view and A/B");
            var packet = first.Packets.Single(); model.Select(packet); model.SelectProtocol(packet, spi ? 1 : 0);
            var selected = model.SelectedProtocol;
            if (spi)
            {
                vm.Target = vm.Targets[0]; Configure(vm.Draft, expected);
                await vm.DecodeCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(60));
                check(model.Capture!.Packets.Count == 2 && model.Capture.DecoderSources!.Select(s => s.Id).ToHashSet().SetEquals(["spi", "spi-1"])
                    && ReferenceEquals(packet, model.Selected) && model.SelectedProtocol == selected,
                    "Adding a second native SPI instance keeps the selected first instance and its direction");
                var originalSecond = model.Capture.Packets.Single(p => p.Decoder == "spi-1");
                await vm.DecodeCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(60));
                check(model.Capture.Packets.Count == 2 && !ReferenceEquals(originalSecond, model.Capture.Packets.Single(p => p.Decoder == "spi-1"))
                    && ReferenceEquals(packet, model.Selected), "Re-decoding a native SPI instance replaces only its own records");
            }
            using var workspace = new TestWorkspace(); string path = Path.Combine(workspace.Root, "serial.dsview2");
            var state = model.SnapshotState(new(channel, 0), channel);
            await AnalysisFile.WriteAsync(path, new(AnalysisDocument.FileFormat, AnalysisDocument.CurrentVersion, "Workflow fixture", model.Capture!, state));
            var reopened = new Workbench(); reopened.RestoreAnalysis(await AnalysisFile.ReadAsync(path));
            check(JsonSerializer.Serialize(reopened.Capture) == JsonSerializer.Serialize(model.Capture)
                && reopened.A == model.A && reopened.B == model.B && JsonSerializer.Serialize(reopened.SelectedProtocol) == JsonSerializer.Serialize(model.SelectedProtocol)
                && reopened.Start == model.Start && reopened.End == model.End,
                $"Native {(spi ? "SPI" : "UART")} workflow saves and reopens all values, options, instances, direction selection, A/B and viewport");
        }
    }
    private static void Configure(DecoderDraft draft, DecoderSource source)
    {
        draft.Protocol = source.EffectiveProtocol == "spi" ? "SPI" : "UART";
        foreach (var input in source.Inputs) DecoderDraftChecks.Map(draft, input.Role, input.Channel);
        if (source.Spi is { } spi) { draft.Bits = spi.WordSize.ToString(); draft.Mode = spi.Mode; draft.Order = spi.BitOrder; draft.Polarity = spi.ChipSelectPolarity; }
        if (source.Uart is { } uart) { draft.Baud = uart.BaudRate.ToString(); draft.Bits = uart.DataBits.ToString(); draft.Parity = uart.Parity; draft.Order = uart.BitOrder; draft.Inverted = uart.Inverted; }
    }
}
