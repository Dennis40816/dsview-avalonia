// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DSView.Desktop;
using DSView.Domain;
using DSView.Infrastructure;

internal static class DslImportChecks
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static DslImportMetadata Meta(long count = 130, int[]? ids = null)
    {
        ids ??= [1, 7, 23, 31];
        return new(1, "cross64-le-lsb", true, 10_000_000, count, (count + 63) / 64 * ids.Length * 8, 0, -28_800_000,
            ids.Select(id => new DslChannel(id, $"Signal {id}")).ToArray());
    }
    private static byte[] Words(params ulong[] words)
    {
        var bytes = new byte[words.Length * 8];
        for (int i = 0; i < words.Length; i++) BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(i * 8, 8), words[i]);
        return bytes;
    }
    private static Task<DslImportResult> Convert(byte[] bytes, DslImportMetadata metadata, CancellationToken token = default) =>
        DslCrossDataAdapter.ReadAsync(new MemoryStream(bytes), metadata, "量測.dsl", new string('a', 64), token);
    public static async Task RunAsync(Action<bool, string> check, string? configuration)
    {
        byte[] raw = Words(0x8000000000000001, 0x7fffffffffffffff, 0, ulong.MaxValue,
            1, ulong.MaxValue, 0, ulong.MaxValue, 0xfffffffffffffffd, 0xfffffffffffffffc, 0xfffffffffffffffc, 3);
        var metadata = Meta(); var imported = await Convert(raw, metadata); var capture = imported.Capture;
        check(capture.Signals.Select(s => s.Id).SequenceEqual(new[] { 1, 7, 23, 31 }) && capture.Signals[0].Initial
            && capture.Signals[0].Edges.SequenceEqual(new Edge[] { new(1, false), new(63, true), new(65, false), new(128, true), new(129, false) }),
            "Cross64 preserves sparse IDs, initial level, little-endian bit order and transitions across words without an edge at sample0");
        check(capture.Signals[1].Edges.SequenceEqual(new Edge[] { new(63, false), new(64, true), new(128, false) })
            && !capture.Signals[2].Initial && capture.Signals[2].Edges.Count == 0 && capture.Signals[3].Initial && capture.Signals[3].Edges.Count == 0,
            "Cross64 retains a real sample64 transition and constant high/low channels while ignoring final-word padding");
        check(capture.MeasureAt(1, 64) is { WidthSamples: 2, PeriodSamples: 65 } && capture.Packets.Count == 0
            && capture.SourceDescription.Contains("Stored trigger position: 0 samples") && capture.SourceDescription.Contains("-28800000 Unix milliseconds"),
            "Imported timing derives from raw edges and preserves zero trigger position and signed epoch metadata without invented packets");
        var original = new Workbench(); original.Load(await DemoCaptureSource.LoadAsync()); original.Pick(0, 25025, 0);
        var originalCapture = original.Capture; var originalA = original.A;
        var document = Workbench.CreateImportedAnalysis(imported, original.Groups.Where(g => g.Name != "All signals").ToArray(), new() { [1] = "#FFFF00" });
        check(ReferenceEquals(original.Capture, originalCapture) && original.A == originalA, "Preparing import does not mutate the current analysis");
        var view = new Workbench(); view.RestoreAnalysis(document);
        check(view.Group.Name == "All signals" && view.Signals.Count == 4 && view.Decoders.Count == 0 && view.A == null && view.Selected == null
            && view.Start == 0 && view.End == 130 && document.State.EdgeChannel == 1 && view.Colors[1] == "#FFFF00", "Imported initial view shows every captured physical signal without decoder rows or inherited analysis selection");
        string json = JsonSerializer.Serialize(metadata, Json);
        check(DslCrossDataAdapter.ReadMetadata(json).Channels.Select(c => c.Id).SequenceEqual(metadata.Channels.Select(c => c.Id)), "Versioned native metadata is read with its exact channel mapping");
        foreach (var (bad, label) in new[] { (json.Replace("\"version\":1", "\"version\":1,\"version\":1"), "duplicate key"),
            (json.Replace("\"complete\":true,", ""), "missing completion"), (json + "{}", "trailing JSON"),
            (json.Replace("\"version\":1", "\"version\":1,\"extra\":0"), "unknown property") })
            await Reject(() => Task.FromResult(DslCrossDataAdapter.ReadMetadata(bad)), "Metadata rejects " + label);
        foreach (var bad in new[] { metadata with { Version = 2 }, metadata with { Layout = "sample-interleaved" }, metadata with { Complete = false },
            metadata with { SampleRate = 0 }, metadata with { SampleCount = 0 }, metadata with { TriggerPosition = 131 },
            metadata with { Channels = [new(1,"One"),new(1,"Duplicate"),new(23,"Third"),new(31,"Last")] },
            metadata with { Channels = [new(7,"Seven"),new(1,"One"),new(23,"Third"),new(31,"Last")] },
            metadata with { Channels = [new(1," "),new(7,"Seven"),new(23,"Third"),new(31,"Last")] } })
            await Reject(() => Convert(raw, bad), "Invalid import bounds/completion/layout/channel contract is rejected");
        await Reject(() => Convert(raw[..^1], metadata), "A truncated raw payload cannot become a valid capture");
        await Reject(() => Convert([..raw,0], metadata), "Extra raw payload is rejected rather than silently trimmed");
        var absent = await Convert(raw, metadata with { TriggerPosition = null, TriggerTimeUnixMs = null });
        var end = await Convert(raw, metadata with { TriggerPosition = 130, TriggerTimeUnixMs = long.MinValue });
        check(absent.Capture.SourceDescription.Contains("position: not recorded") && end.Capture.SourceDescription.Contains("position: 130 samples")
            && end.Capture.SourceDescription.Contains(long.MinValue.ToString()), "Absent, zero and end-boundary trigger provenance remain distinct without unsafe date conversion");
        var escaped = metadata with { Channels = metadata.Channels.Select(c => c with { Name = "資料 \"clock\"\\path" }).ToArray() };
        check(DslCrossDataAdapter.ReadMetadata(JsonSerializer.Serialize(escaped, Json)).Channels[0].Name == escaped.Channels[0].Name,
            "Unicode, quotes, backslashes and duplicate display names retain physical identity in metadata");
        using (var cancel = new CancellationTokenSource())
        using (var stream = new CancelReadStream(new byte[8192], cancel))
        {
            try { await DslCrossDataAdapter.ReadAsync(stream, Meta(65536, [1]), "constant.dsl", new('b',64), cancel.Token); throw new Exception("Conversion ignored cancellation"); }
            catch (OperationCanceledException) { check(stream.Reads == 1, "Constant-signal conversion observes cancellation after reading without waiting for a transition"); }
        }
        foreach (var (channels, count, accepted) in new[] { (1, 2_000_001L, true), (1, 2_000_002L, false), (2, 1_000_001L, true), (2, 1_000_002L, false) })
        {
            var meta = Meta(count, Enumerable.Range(0, channels).ToArray()); var bytes = new byte[meta.PayloadBytes]; Array.Fill(bytes, (byte)0xAA);
            if (accepted) { var value = await Convert(bytes, meta); check(value.Capture.Signals.Sum(s => s.Edges.Count) == 2_000_000, "Exact global transition limit is accepted across " + channels + " channel(s)"); }
            else await Reject(() => Convert(bytes, meta), "Global transition limit is enforced before excessive edge allocation across " + channels + " channel(s)");
        }
        string directory = Path.Combine(Path.GetTempPath(), "DSView2-import-checks-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var created = new List<string>();
        try
        {
            string saved = Path.Combine(directory, "imported.dsview2"); created.Add(saved);
            await AnalysisFile.WriteAsync(saved, document); var restored = await AnalysisFile.ReadAsync(saved);
            check(restored.SourceName == imported.SourceName && restored.Capture.SourceDescription == capture.SourceDescription
                && restored.Capture.SampleRate == capture.SampleRate && restored.Capture.SampleCount == capture.SampleCount && restored.Capture.Signals.Count == capture.Signals.Count
                && restored.Capture.Signals.Zip(capture.Signals).All(p => p.First.Id == p.Second.Id && p.First.Initial == p.Second.Initial && p.First.Edges.SequenceEqual(p.Second.Edges)),
                "Import analysis round trip preserves raw sparse signals and provenance without native worker or original capture");
            if (configuration == null) { Console.WriteLine("SKIP optional native import checks: pass --native-dsl <configuration>."); return; }
            var options = NativeDslSource.ReadConfiguration(configuration);
            string[] existingStaging = Directory.Exists(options.StagingDirectory) ? Directory.GetFileSystemEntries(options.StagingDirectory) : [];
            bool StagingRestored() => new HashSet<string>(Directory.GetFileSystemEntries(options.StagingDirectory), StringComparer.OrdinalIgnoreCase).SetEquals(existingStaging);
            string input = Path.Combine(directory, "原始 量測.dsl"); created.Add(input);
            string header = "[version]\nversion = 3\n[header]\ndriver = test-fixture\ndevice mode = 0\ncapturefile = data\ntotal samples = 192\ntotal probes = 2\ntotal blocks = 1\nsamplerate = 10 MHz\ntrigger pos = 0\ntrigger time = -28800000\nprobe12 = 資料\nprobe1 = SCL \"clock\"\\\\path\ntrigger0 = R\n";
            WriteZip(input, header, [("L-12/0",Words(0x7fffffffffffffff,ulong.MaxValue,0)),("L-1/0",Words(0x8000000000000001,1,1))]);
            string before = ConvertHash(input);
            var native = await NativeDslSource.LoadAsync(input, options);
            check(native.Capture.SampleRate == 10_000_000 && native.Capture.SampleCount == 192 && native.Capture.Signals.Select(s => s.Id).SequenceEqual(new[] { 1,12 })
                && native.Capture.Signals[0].Name == "SCL \"clock\"\\path" && native.Capture.Signals[1].Name == "資料"
                && native.Capture.Signals[0].Edges.SequenceEqual(capture.Signals[0].Edges) && native.Capture.Signals[1].Initial
                && native.Capture.Signals[1].Edges.SequenceEqual(new Edge[] { new(63,false),new(64,true),new(128,false) }), "Actual native import preserves Unicode source/name, escaped labels, reordered sparse channels and exact sample seams");
            check(native.Capture.SourceDescription.Contains(before) && ConvertHash(input) == before && StagingRestored(),
                "Native import leaves the original unchanged and removes staging only after worker exit");
            await Reject(() => NativeDslSource.LoadAsync(input, options with { WorkerSha256 = new('0',64) }), "Changed capture reader identity is rejected");
            string invalid = Path.Combine(directory,"invalid.dsl"); created.Add(invalid);
            WriteZip(invalid, header.Replace("total samples = 192","total samples = 256"), [("L-12/0",new byte[24]),("L-1/0",new byte[24])]);
            await Reject(() => NativeDslSource.LoadAsync(invalid, options), "Actual native worker rejects metadata/payload disagreement");
            foreach (string stage in new[] { "Copying capture…", "Preparing waveforms…" })
            {
                using var cancel = new CancellationTokenSource();
                try { await NativeDslSource.LoadAsync(input, options, cancel.Token, new InlineProgress(s => { if(s==stage) cancel.Cancel(); })); throw new Exception("Stage cancellation ignored"); }
                catch (OperationCanceledException) { check(StagingRestored(), "Cancellation at " + stage + " retains no partial staging"); }
            }
            string large = Path.Combine(directory,"large.dsl"); created.Add(large);
            WriteZip(large,"[version]\nversion = 3\n[header]\ndevice mode = 0\ncapturefile = data\ntotal samples = 32000000\ntotal probes = 16\ntotal blocks = 1\nsamplerate = 10 MHz\n" + string.Join("\n",Enumerable.Range(0,16).Select(i=>$"probe{i} = D{i}")),
                Enumerable.Range(0,16).Select(i=>($"L-{i}/0", new byte[4_000_000])).ToArray());
            using (var cancel = new CancellationTokenSource())
            {
                string workerName = Path.GetFileNameWithoutExtension(options.Worker);
                var prior = Process.GetProcessesByName(workerName).Select(p => { using (p) return p.Id; }).ToHashSet();
                Process? observed = null;
                try
                {
                    await NativeDslSource.LoadAsync(large, options, cancel.Token, new InlineProgress(stage =>
                    {
                        if (stage != "Reading capture…") return;
                        foreach (var p in Process.GetProcessesByName(workerName))
                        {
                            if (observed == null && !prior.Contains(p.Id) && !p.HasExited && string.Equals(p.MainModule?.FileName, Path.GetFullPath(options.Worker), StringComparison.OrdinalIgnoreCase)) observed = p;
                            else p.Dispose();
                        }
                        cancel.Cancel();
                    }));
                    throw new Exception("Live worker cancellation ignored");
                }
                catch (OperationCanceledException) { observed?.Refresh(); check(observed is { HasExited: true } && StagingRestored(), "Cancelling the newly observed import worker confirms its exit before staging cleanup"); }
                finally { observed?.Dispose(); }
            }
            var again = await NativeDslSource.LoadAsync(input, options);
            check(again.Capture.Signals[0].Edges.SequenceEqual(native.Capture.Signals[0].Edges), "Native import restarts cleanly after cancellation");
        }
        finally { foreach (string path in created) if (File.Exists(path)) File.Delete(path); Directory.Delete(directory); }
        async Task Reject(Func<Task> action, string name)
        { try { await action(); throw new Exception(name); } catch (InvalidDataException) { check(true,name); } }
    }
    private static string ConvertHash(string path) { using var file = File.OpenRead(path); return System.Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant(); }
    private static void WriteZip(string path, string header, (string Name, byte[] Data)[] planes)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var file = archive.CreateEntry("header").Open()) file.Write(Encoding.UTF8.GetBytes(header));
        foreach (var (name, data) in planes) { using var file = archive.CreateEntry(name).Open(); file.Write(data); }
    }
    private sealed class InlineProgress(Action<string> action) : IProgress<string> { public void Report(string value) => action(value); }
    private sealed class CancelReadStream(byte[] bytes, CancellationTokenSource cancel) : MemoryStream(bytes)
    {
        public int Reads { get; private set; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellation = default)
        { int read = await base.ReadAsync(buffer, cancellation); Reads++; cancel.Cancel(); return read; }
    }
}
