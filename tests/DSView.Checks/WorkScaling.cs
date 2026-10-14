// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Collections;
using System.Diagnostics;
using System.Text.Json;
using DSView.Desktop;
using DSView.Domain;

internal sealed class CountedEdges(Edge[] values) : IReadOnlyList<Edge>
{
    public long Reads { get; private set; }
    public long Enumerated { get; private set; }
    public int Count => values.Length;
    public Edge this[int index] { get { Reads++; return values[index]; } }
    public void Reset() { Reads = Enumerated = 0; }
    public IEnumerator<Edge> GetEnumerator() { foreach (var value in values) { Enumerated++; yield return value; } }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

internal static class WorkScaling
{
    public static void Run(Action<bool, string> check)
    {
        var results = new List<object>(); long smallerReads = 0;
        foreach (int size in new[] { 100_000, 1_000_000 })
        {
            var edges = new CountedEdges(Enumerable.Range(0, size).Select(i => new Edge((i + 1L) * 2, (i & 1) == 0)).ToArray());
            var signal = new Signal(0, "Clock", false, edges);
            long samples = size * 2L + 2;
            WaveformProjection.Create(signal, 0, samples, 1024); // JIT / allocation warm-up.
            edges.Reset(); long allocated = GC.GetAllocatedBytesForCurrentThread(); var watch = Stopwatch.StartNew();
            var trace = WaveformProjection.Create(signal, 0, samples, 1024);
            watch.Stop(); allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            long reads = edges.Reads;
            check(trace.Runs.Sum(r => r.Count) == size && trace.Runs.Count <= 1024 && edges.Enumerated == 0 && reads < 30_000, $"{size:N0} edges project into at most 1024 activity runs without source enumeration");
            if (size == 100_000) smallerReads = reads;
            else check(reads < smallerReads * 1.5, "Tenfold source growth increases projection index reads by less than 1.5x");
            results.Add(new { size, traceRuns = trace.Runs.Count, indexReads = reads, elapsedMs = watch.Elapsed.TotalMilliseconds, allocatedBytes = allocated });
            if (size != 1_000_000) continue;
            var capture = new Capture(50_000_000, samples, [signal], []) { Gaps = [new(800_000, 1_200_000)] };
            edges.Reset(); int observed = capture.ObservedEdgeCount(signal, 0, samples);
            check(observed == 800_000 && edges.Reads < 100 && edges.Enumerated == 0, "Million-edge gapped activity count uses boundary searches, not 200000 missing transitions");
            long countReads = edges.Reads; edges.Reset();
            var next = capture.ObservedEdgeAtOrAfter(signal, 800_000); var previous = capture.ObservedEdgeBefore(signal, 1_200_000);
            check(next?.Sample == 1_200_000 && previous?.Sample == 799_998 && edges.Reads < 150 && edges.Enumerated == 0, "Previous/next observed edge jumps across the full missing interval");
            long navigationReads = edges.Reads; edges.Reset();
            var sources = Enumerable.Range(0, 32).Select(id => signal with { Id = id }).ToArray();
            var bins = capture.ActivityBins(sources, 1024);
            check(bins.Any(x => x) && bins.Any(x => !x) && edges.Reads < 400_000 && edges.Enumerated == 0, "32-channel overview work stays bounded by bins and source lookups");
            long overviewReads = edges.Reads; edges.Reset();
            int totalRuns = sources.Sum(s => WaveformProjection.Create(s, 0, samples, 1024).Runs.Count);
            check(totalRuns <= 32 * 1024 && edges.Reads < 32 * 30_000 && edges.Enumerated == 0, "32-channel trace preparation remains bounded by plot width");
            results.Add(new { channels = 32, sharedImmutableEdgeArray = true, observed, countReads, navigationReads, overviewReads, totalRuns, traceReads = edges.Reads });
        }
        Console.WriteLine("WORK_SCALING " + JsonSerializer.Serialize(results));
    }
}
