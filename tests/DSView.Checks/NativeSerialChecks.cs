// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using DSView.Checks.Support;
using DSView.Desktop;
using DSView.Domain;
using DSView.Infrastructure;

internal static class NativeSerialChecks
{
    public static async Task RunAsync(Action<bool, string> check, string? configuration, string? evidence)
    {
        if (evidence == null) Console.WriteLine("SKIP saved native SPI/UART evidence: supply --serial-fixtures directory");
        else await ReplayAsync(check, evidence);
        if (configuration == null) { Console.WriteLine("SKIP serial native process: supply --native-serial configuration"); return; }
        var runtime = NativeSerialSource.ReadConfiguration(configuration);
        var (raw, source) = SpiStimulus();
        var result = await NativeSerialSource.DecodeAsync(raw, source, runtime); var packet = result.Packets.Single();
        check(packet.Spi!.Words.Single() is { Mosi.Hex: "1A5", Miso.Hex: "13C" } && packet.Spi.Completion == SpiCompletion.ChipSelectReleased,
            "Live native SPI preserves independent 9-bit values and CS completion on sparse physical channels");
        var capture = result.ToCapture(); var model = new Workbench(); model.Load(capture); model.ShowAll();
        var lane = model.DecoderLanes.Single(l => l.Direction == "MISO"); model.SelectProtocol(packet, lane.Items(capture, packet).Single().Index);
        using var files = new TestWorkspace(); string path = Path.Combine(files.Root, "spi.dsview2");
        await AnalysisFile.WriteAsync(path, new(AnalysisDocument.FileFormat, 5, "Native SPI", capture, model.SnapshotState(new(null, 0), null)));
        model.RestoreAnalysis(await AnalysisFile.ReadAsync(path));
        check(model.SelectedProtocol?.Serial is { Direction: "MISO", Word.Hex: "13C" } && model.Capture!.SourceDescription == raw.SourceDescription
            && model.Decoders.Single().Channels.SequenceEqual(new[] { 19, 3, 7, 11 }), "Native SPI result locates both data directions and retains value, mapping and selection after reopen");
        var (uartRaw, uartSource) = UartStimulus(); var uart = await NativeSerialSource.DecodeAsync(uartRaw, uartSource, runtime);
        check(uart.Packets.Single().Uart is { Data.Hex: "1A5", Completion: UartCompletion.StopObserved, Valid: true }, "Live native UART decodes a complete 9-bit frame from sparse D23");
        var replacement = await NativeSerialSource.DecodeAsync(capture, source, runtime);
        check(replacement.ToCapture().Packets.Count == capture.Packets.Count && ReferenceEquals(replacement.Source, capture), "Repeated native serial decode replaces only its requested instance");
        var second = await NativeSerialSource.DecodeAsync(capture, source with { Id = "sensor-second" }, runtime);
        check(second.ToCapture().Packets.Count == 2 && second.ToCapture().Packets.Any(p => ReferenceEquals(p, packet))
            && second.ToCapture().Packets.Select(p => p.Id).Distinct().Count() == 2, "A second native SPI instance preserves unrelated packet identity and source bindings");
        bool optional = true;
        foreach (string role in new[] { "mosi", "miso" })
        {
            var oneWay = source with { Inputs = source.Inputs.Where(i => i.Role == "clk" || i.Role == role).ToArray() };
            var one = await NativeSerialSource.DecodeAsync(raw, oneWay, runtime); var word = one.Packets.Single().Spi!.Words.Single();
            optional &= one.Packets.Single().Spi!.Completion == SpiCompletion.WithoutChipSelect
                && (role == "mosi" ? word.Mosi?.Hex == "1A5" && word.Miso == null : word.Miso?.Hex == "13C" && word.Mosi == null);
        }
        check(optional, "Native SPI optional planes remain unmapped and no-CS data has no invented transfer boundary");
        var (changingRaw, changingSource) = SpiStimulus(true);
        var changing = await NativeSerialSource.DecodeAsync(changingRaw, changingSource, runtime);
        var changingWords = changing.Packets.Single().Spi!.Words;
        check(changingWords.Select(w => w.Mosi!.Hex).SequenceEqual(new[] { "1A5", "055" })
            && changingWords[0].Mosi!.Span.End > changingWords[1].Mosi!.Span.Start,
            "Native SPI retains correct words when an accelerated clock makes estimated annotation ends overlap");
        var rejectedStart = uartRaw with { SampleCount = 64, Signals = [new(23, "RX", true, [new(16, false), new(20, true)])] };
        var rejectedFrame = await NativeSerialSource.DecodeAsync(rejectedStart, uartSource, runtime);
        check(rejectedFrame.Packets.Single().Uart is { Data: null, Completion: UartCompletion.RejectedStart, Valid: false }
            && rejectedFrame.Packets.Single().Diagnostics.Single().Code == "invalid-start", "A rejected UART start retains its diagnostic without promoting a stale native FRAME value to data");
        bool wrongHash = false;
        try { await NativeSerialSource.DecodeAsync(raw, source, runtime with { WorkerSha256 = new string('0', 64) }); }
        catch (InvalidDataException) { wrongHash = true; }
        check(wrongHash, "Changed serial worker identity is rejected before execution");
        using var cancel = new CancellationTokenSource(); cancel.Cancel(); bool cancelled = false;
        try { await NativeSerialSource.DecodeAsync(raw, source, runtime, cancel.Token); } catch (OperationCanceledException) { cancelled = true; }
        check(cancelled, "Cancelled serial request does not start a worker or publish results");
    }
    private static async Task ReplayAsync(Action<bool, string> check, string evidence)
    {
        int accepted = 0; var results = new Dictionary<string, SerialDecodeResult>();
        foreach (string path in Directory.EnumerateFiles(evidence, "*.stdout.json").Order())
        {
            if (new FileInfo(path).Length == 0) continue;
            string json = await File.ReadAllTextAsync(path); using var doc = JsonDocument.Parse(json); var root = doc.RootElement;
            if (!root.TryGetProperty("schema", out _)) continue;
            var options = root.GetProperty("options"); bool spi = root.GetProperty("decoder").GetString() == "1:spi";
            if (!spi && options.GetProperty("stopBits").GetDouble() != 1) continue; // Explicit unsupported upstream limitation.
            string name = Path.GetFileName(path).Replace(".stdout.json", "", StringComparison.Ordinal);
            var raw = ReadPlanes(Path.Combine(evidence, name + ".raw"), root.GetProperty("sampleCount").GetInt64(), root.GetProperty("sampleRate").GetInt64(), spi ? 4 : 1);
            var source = Source(options, spi);
            SerialDecodeResult result;
            try { result = spi ? SpiEventAdapter.Read(raw, source, json) : UartEventAdapter.Read(raw, source, json); }
            catch (Exception ex) { throw new InvalidDataException("Replay failed: " + name, ex); }
            var capture = result.ToCapture(); var model = new Workbench(); model.Load(capture); model.ShowAll();
            AnalysisFile.Validate(new(AnalysisDocument.FileFormat, 5, name, capture, model.SnapshotState(new(null, 0), null)));
            results[name] = result; accepted++;
        }
        check(accepted >= 100, $"Saved native serial regression accepts {accepted} complete supported envelopes with matching raw samples");
        check(results["spi-word128"].Packets.Single().Spi!.Words.Single() is { Mosi.Hex: "FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFD", Miso.Hex: "80000000000000000000000000000001" }, "Native 128-bit SPI values retain every significant digit");
        check(results["uart-word128"].Packets.Single().Uart!.Data!.Hex == "FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFD", "Native 128-bit UART value retains every significant digit");
        check(results["uart-parity-details-placeholder"].Packets.Single().Diagnostics.Single().Message == "UART parity error."
            && results["uart-parity-details-placeholder"].Packets.Single().Uart!.Valid == false, "Parity error is retained without presenting upstream placeholder numbers as observations");
        check(results["uart-break"].Packets.Count == 2 && results["uart-break"].Packets.Count(p => p.Uart?.Completion == UartCompletion.Break) == 1
            && results["uart-break"].Packets.Any(p => p.Uart?.Valid == false), "Break keeps its full interval separately from the preceding invalid frame");
        check(results["uart-eof-before-data-complete"].Packets.Single().Uart is { Data: null, Completion: UartCompletion.Incomplete, Valid: null }
            && results["spi-partial"].Packets.Single().Spi!.Words.Length == 0, "Truncated UART and SPI words never become valid zero-valued data");
        string spiPath = Path.Combine(evidence, "spi-mode0-msb-first.stdout.json"); string original = await File.ReadAllTextAsync(spiPath);
        using var baseDoc = JsonDocument.Parse(original); var baseRoot = baseDoc.RootElement;
        var sourceBase = Source(baseRoot.GetProperty("options"), true);
        var rawBase = ReadPlanes(Path.Combine(evidence, "spi-mode0-msb-first.raw"), 200, 1_600_000, 4);
        int rejected = 0;
        foreach (Action<JsonNode> mutate in new Action<JsonNode>[] {
            n => n["events"]![3]!["record"]![1] = 164,
            n => n["events"]![7]!["record"]![1]![0]![2] = 164,
            n => n["events"]![2]!["record"]![1]![0]![0] = 0,
            n => n["events"]![3]!["sequence"] = 9,
            n => n["options"]!["cpha"] = 1,
            n => n["events"]!.AsArray().RemoveAt(7),
            n => n["events"]![2]!["record"]![0] = "UNRECOGNIZED",
            n => n["unexpected"] = 1 })
        {
            var node = JsonNode.Parse(original)!; mutate(node);
            try { SpiEventAdapter.Read(rawBase, sourceBase, node.ToJsonString()); }
            catch (Exception ex) when (ex is InvalidDataException or JsonException) { rejected++; }
        }
        try { SpiEventAdapter.Read(rawBase, sourceBase, original.Replace("\"schema\":1", "\"schema\":1,\"schema\":1", StringComparison.Ordinal)); }
        catch (Exception ex) when (ex is InvalidDataException or JsonException) { rejected++; }
        check(rejected == 9, "Serial admission rejects contradictory DATA/BITS/TRANSFER, sequence, options, missing completion, unknown and duplicate fields");
        string stopJson = await File.ReadAllTextAsync(Path.Combine(evidence, "uart-stop-error.stdout.json"));
        var stopNode = JsonNode.Parse(stopJson)!;
        while (stopNode["events"]!.AsArray().Count > 3) stopNode["events"]!.AsArray().RemoveAt(3);
        using var stopDoc = JsonDocument.Parse(stopJson); var stopRoot = stopDoc.RootElement;
        var stopRaw = ReadPlanes(Path.Combine(evidence, "uart-stop-error.raw"), stopRoot.GetProperty("sampleCount").GetInt64(), 1_600_000, 1);
        bool orphanRejected = false;
        try { UartEventAdapter.Read(stopRaw, Source(stopRoot.GetProperty("options"), false), stopNode.ToJsonString()); }
        catch (InvalidDataException) { orphanRejected = true; }
        check(orphanRejected, "A truncated native stop-error event sequence is rejected rather than accepted as ordinary incomplete UART");
    }
    private static DecoderSource Source(JsonElement options, bool spi)
    {
        string Text(string key) => options.GetProperty(key).GetString()!;
        int Number(string key) => options.GetProperty(key).GetInt32();
        if (!spi) return new("test-uart", [new("data", 0)]) { Protocol = "uart", Uart = new(Number("baudRate"), Number("dataBits"), Text("parity"), Number("stopBits"), Text("bitOrder"), options.GetProperty("invert").GetBoolean()) };
        int mask = Number("channelMask"); var inputs = new List<DecoderInput> { new("clk", 0) };
        if ((mask & 1) != 0) inputs.Add(new("miso", 1)); if ((mask & 2) != 0) inputs.Add(new("mosi", 2)); if ((mask & 4) != 0) inputs.Add(new("cs", 3));
        return new("test-spi", inputs.ToArray()) { Protocol = "spi", Spi = new(Number("cpol") * 2 + Number("cpha"), Text("bitOrder"), Number("wordSize"), Text("csPolarity")) };
    }
    private static Capture ReadPlanes(string path, long samples, long rate, int count)
    {
        byte[] bytes = File.ReadAllBytes(path); int plane = (int)((samples + 7) / 8);
        if (bytes.Length != plane * count) throw new InvalidDataException("Fixture plane length mismatch.");
        var signals = new List<Signal>();
        for (int id = 0; id < count; id++)
        {
            bool initial = (bytes[id * plane] & 1) != 0, prior = initial; var edges = new List<Edge>();
            for (int i = 1; i < samples; i++)
            {
                bool high = (bytes[id * plane + i / 8] & (1 << (i % 8))) != 0;
                if (high != prior) { edges.Add(new(i, high)); prior = high; }
            }
            signals.Add(new(id, "Fixture " + id, initial, edges));
        }
        return new(rate, samples, signals, []) { DecoderSources = [], SourceDescription = "Independent raw stimulus replay; no hardware capture." };
    }
    private static (Capture Raw, DecoderSource Source) SpiStimulus(bool accelerate = false)
    {
        var clk = new List<Edge>(); var mosi = new List<Edge>(); var miso = new List<Edge>(); bool tx = false, rx = false;
        for (int word = 0; word < (accelerate ? 2 : 1); word++)
        for (int bit = 8; bit >= 0; bit--)
        {
            long time = word == 0 ? 20 + (8 - bit) * 10 : 105 + (8 - bit) * 5;
            bool nextTx = ((word == 0 ? 0x1A5 : 0x055) & (1 << bit)) != 0, nextRx = ((word == 0 ? 0x13C : 0x102) & (1 << bit)) != 0;
            if (nextTx != tx) { mosi.Add(new(time, nextTx)); tx = nextTx; } if (nextRx != rx) { miso.Add(new(time, nextRx)); rx = nextRx; }
            clk.Add(new(time + 2, true)); clk.Add(new(time + (word == 0 ? 6 : 4), false));
        }
        return (new(1_000_000, accelerate ? 184 : 140, [new(3, "TX", false, mosi), new(7, "RX", false, miso), new(11, "CS", true, [new(10, false), new(accelerate ? 160 : 120, true)]), new(19, "CLK", false, clk)], [])
            { DecoderSources = [], SourceDescription = "Independent sparse 9-bit SPI stimulus." },
            new("sensor", [new("clk", 19), new("mosi", 3), new("miso", 7), new("cs", 11)]) { Protocol = "spi", Spi = new(0, "msb-first", 9, "active-low") });
    }
    private static (Capture Raw, DecoderSource Source) UartStimulus()
    {
        var edges = new List<Edge> { new(16, false) }; bool prior = false;
        for (int bit = 0; bit < 9; bit++)
        { bool next = (0x1A5 & (1 << bit)) != 0; if (next != prior) { edges.Add(new(32 + bit * 16, next)); prior = next; } }
        if (!prior) edges.Add(new(176, true));
        return (new(1_600_000, 224, [new(23, "RX", true, edges)], []) { DecoderSources = [], SourceDescription = "Independent 9-bit UART stimulus." },
            new("console", [new("data", 23)]) { Protocol = "uart", Uart = new(100000, 9, "none", 1, "lsb-first", false) });
    }
}
