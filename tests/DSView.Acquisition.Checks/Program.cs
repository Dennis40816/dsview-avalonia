// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using DSView.Domain;
using DSView.Infrastructure;

int passed = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); passed++; }
TaskCompletionSource Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);
AcquisitionPlan Plan(long count = 20) => new(AcquisitionSourceKind.Demo, "Controlled samples", 1_000_000, count, [new(0, "Clock")]);
Task<AcquisitionResult> Run(ScriptSource source, AcquisitionLimits? limits = null) => RunOwned(source, limits);
async Task<AcquisitionResult> RunOwned(IAcquisitionSource source, AcquisitionLimits? limits = null)
{
    await using var session = new AcquisitionSession(source, limits);
    return await session.RunAsync().WaitAsync(TimeSpan.FromSeconds(10));
}
ScriptSource Script(Func<AcquisitionWriter, AcquisitionTermination> write, long count = 20) =>
    new(Plan(count), (output, _, _) => Task.FromResult(write(output)));

var originalChannels = new[] { new AcquisitionChannel(31, "Last"), new AcquisitionChannel(0, "First") };
var ordered = new AcquisitionPlan(AcquisitionSourceKind.Demo, "Physical order", 100, 10, originalChannels);
originalChannels[0] = new(1, "Changed");
Check(ordered.Channels.Select(c => c.Id).SequenceEqual(new[] { 0, 31 }) && ordered.ChannelMask == 0x80000001,
    "Applied plan copies physical IDs in order, including D31, independently of caller mutations");
foreach (var (name, invalid) in new (string, Action)[] {
    ("unknown source", () => new AcquisitionPlan((AcquisitionSourceKind)99, "Unknown", 100, 10, [new(0,"D0")])),
    ("zero rate", () => new AcquisitionPlan(AcquisitionSourceKind.Demo, "Clock", 0, 10, [new(0,"D0")])),
    ("zero limit", () => new AcquisitionPlan(AcquisitionSourceKind.Demo, "Clock", 100, 0, [new(0,"D0")])),
    ("duplicate physical ID", () => new AcquisitionPlan(AcquisitionSourceKind.Demo, "Clock", 100, 10, [new(0,"D0"),new(0,"Duplicate")])) })
{
    bool rejected = false; try { invalid(); } catch (ArgumentException) { rejected = true; }
    Check(rejected, $"Applied plan rejects {name}");
}

// Copy ownership and transitions across uneven batch boundaries.
var ownership = await Run(Script(w =>
{
    w.Running(); uint[] first = [0,0,1,1,1,0,0];
    w.TryWrite(0, first); Array.Fill(first, 1u);
    w.TryWrite(7, [0,1,1,0,0,1,1,0,0,1,1,0,0]);
    return AcquisitionTermination.Completed;
}));
Check(ownership.CanPublish && ownership.RawCapture is { SampleCount: 20, Packets.Count: 0, DecoderSources.Length: 0 }
    && !ownership.RawCapture.Signals[0].Initial
    && ownership.RawCapture.Signals[0].Edges.Select(e => e.Sample).SequenceEqual(new long[] {2,5,8,10,12,14,16,18}),
    "Borrowed buffers are copied; transitions at batch boundaries use actual sample positions without prefilled packets");

foreach (int count in new[] { 0, 1, 9, 10 })
{
    var result = await Run(Script(w => { w.Running(); if (count > 0) w.TryWrite(0, new uint[count]); return AcquisitionTermination.Completed; }, Math.Max(1, count)));
    Check(result.RetainedSamples == count && result.RawCapture?.SampleCount == (count == 0 ? null : (long?)count)
        && result.CanPublish == (count >= 10) && (count != 0 || result.Termination == AcquisitionTermination.DataLoss),
        $"{count}-sample acquisition retains honest counts and never pads to the workbench minimum");
}

var early = await Run(Script(w => { w.Running(); w.TryWrite(0, new uint[12]); return AcquisitionTermination.Completed; }));
Check(early.Termination == AcquisitionTermination.DataLoss && early.RequiresPartialAcceptance && !early.CanPublish && early.RawCapture!.SampleCount == 12,
    "Premature completion retains a marked prefix and requires explicit partial acceptance");
foreach (long wrongStart in new[] { 11L, 13L })
{
    var result = await Run(Script(w => { w.Running(); w.TryWrite(0, new uint[12]); w.TryWrite(wrongStart, [1,1]); return AcquisitionTermination.Completed; }));
    Check(result.Termination == AcquisitionTermination.DataLoss && result.RetainedSamples == 12 && result.RawCapture!.Signals[0].Edges.Count == 0,
        "Overlapping or missing samples terminate the continuous prefix without inventing an edge across loss");
}
foreach (var (name, bad) in new (string, Func<AcquisitionWriter,bool>)[] {
    ("samples before running", w => w.TryWrite(0, [0])),
    ("running twice", w => { w.Running(); return w.Running(); }),
    ("unconfigured channel", w => { w.Running(); return w.TryWrite(0, [2]); }),
    ("excess sample count", w => { w.Running(); return w.TryWrite(0, new uint[21]); }),
    ("empty batch", w => { w.Running(); return w.TryWrite(0, []); }) })
{
    var result = await Run(Script(w => { if (bad(w)) throw new Exception("Malformed source was accepted."); return AcquisitionTermination.Completed; }));
    Check(result.Termination == AcquisitionTermination.Failed && result.RawCapture == null,
        $"Invalid source event ({name}) cannot create a successful capture");
}

var ingress = new AcquisitionWriter(Plan(), new(BatchSamples: 8, QueueBatches: 2));
uint[] borrowed = [0,1];
ingress.Running(); bool firstQueued = ingress.TryWrite(0, borrowed), secondQueued = ingress.TryWrite(2, [1,0]);
Array.Fill(borrowed, 0u); // Mutate before any consumer exists: ownership proof cannot race.
bool overflowQueued = ingress.TryWrite(4, [0,1]);
var batches = new List<SampleBatch>(); await foreach (var batch in ingress.Reader.ReadAllAsync()) batches.Add(batch);
Check(firstQueued && secondQueued && !overflowQueued && ingress.Accepted == 4 && ingress.Fault?.Reason == AcquisitionTermination.Overflow
    && batches.SelectMany(b => b.Samples).SequenceEqual(new uint[] {0,1,1,0}) && !ingress.TryWrite(4,[1]),
    "Queue saturation preserves the owned prefix despite buffer reuse, rejects overflow and closes without waiting for the consumer");

var allChannels = new AcquisitionPlan(AcquisitionSourceKind.Demo, "32-channel boundaries", 1_000_000, 20,
    Enumerable.Range(0,32).Select(id => new AcquisitionChannel(id,$"D{id}")));
var allResult = await RunOwned(new ScriptSource(allChannels, (w, _, _) =>
{
    w.Running(); w.TryWrite(0, [0xffffffff,0xffffffff,0xffffffff,0xffffffff,0xffffffff,0xffffffff,0xffffffff]);
    w.TryWrite(7, [0x7ffffffe,0x7ffffffe,0x7ffffffe,0xffffffff,0xffffffff,0xffffffff,0xffffffff,0xffffffff,0xffffffff,0xffffffff,0xffffffff,0xffffffff,0xffffffff]);
    return Task.FromResult(AcquisitionTermination.Completed);
}));
Check(allResult.CanPublish && allResult.RawCapture!.Signals.Count == 32 && allResult.RawCapture.Signals.All(s => s.Initial)
    && allResult.RawCapture.Signals[0].Edges.SequenceEqual(new Edge[] {new(7,false),new(10,true)})
    && allResult.RawCapture.Signals[31].Edges.SequenceEqual(new Edge[] {new(7,false),new(10,true)})
    && allResult.RawCapture.Signals.Skip(1).Take(30).All(s => s.Edges.Count == 0),
    "Packed 32-channel acquisition preserves D0/D31 changes while constant channels have no invented edges");

var limited = await Run(Script(w => { w.Running(); w.TryWrite(0,[0,1,0,1,0,1,0,1,0,1,0,1,0,1,0,1,0,1,0,1]); return AcquisitionTermination.Completed; }), new(Transitions: 12));
Check(limited.Termination == AcquisitionTermination.Overflow && limited.AcceptedSamples == 20 && limited.RetainedSamples == 13
    && limited.RawCapture!.Signals[0].Edges.Count == 12 && limited.RequiresPartialAcceptance,
    "Transition budget stops on a sample boundary and distinguishes accepted input from retained storage");

var entered = Gate(); var stopSeen = Gate(); var teardown = Gate(); AcquisitionWriter? stoppedWriter = null;
var stoppable = new ScriptSource(Plan(), async (w, stop, abort) =>
{
    stoppedWriter = w; w.Running(); w.TryWrite(0, new uint[12]); entered.SetResult();
    try { await Task.Delay(Timeout.Infinite, stop); } catch (OperationCanceledException) { stopSeen.SetResult(); }
    await teardown.Task.WaitAsync(abort); return AcquisitionTermination.Stopped;
});
await using (var session = new AcquisitionSession(stoppable))
{
    var job = session.RunAsync(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
    Check(ReferenceEquals(job, session.RunAsync()) && session.Progress.Phase == AcquisitionPhase.Running,
        "Repeated Start joins the same operation rather than creating a second producer");
    session.RequestStop(); await stopSeen.Task.WaitAsync(TimeSpan.FromSeconds(10));
    Check(!job.IsCompleted && session.Progress.Phase == AcquisitionPhase.Stopping,
        "Stop request does not report completion while source teardown is pending");
    teardown.SetResult(); var result = await job.WaitAsync(TimeSpan.FromSeconds(10));
    Check(result.Termination == AcquisitionTermination.Stopped && result.RetainedSamples == 12 && result.RequiresPartialAcceptance
        && !stoppedWriter!.TryWrite(12,[1]) && session.Progress.Phase == AcquisitionPhase.Finished,
        "Confirmed stop retains its actual prefix and rejects callbacks after source completion");
}

var cancelEntered = Gate(); var releaseLate = Gate();
await using (var session = new AcquisitionSession(new ScriptSource(Plan(), async (w, _, _) =>
{
    w.Running(); w.TryWrite(0, new uint[12]); cancelEntered.SetResult(); await releaseLate.Task;
    w.TryWrite(12,new uint[8]); return AcquisitionTermination.Completed;
})))
{
    var job = session.RunAsync(); await cancelEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
    try { session.Cancel(); Check(!job.IsCompleted, "Abort waits for a producer that has not yet acknowledged cancellation"); }
    finally { releaseLate.TrySetResult(); }
    var result = await job.WaitAsync(TimeSpan.FromSeconds(10));
    Check(result.Termination == AcquisitionTermination.Cancelled && result.RawCapture == null && result.AcceptedSamples == 12
        && result.RetainedSamples == 0 && !result.CanPublish && !result.RequiresPartialAcceptance,
        "Even a late successful source return cannot publish an aborted capture");
}
var disconnected = await Run(Script(w => { w.Running(); w.TryWrite(0,new uint[12]); return AcquisitionTermination.Disconnected; }));
Check(disconnected.Termination == AcquisitionTermination.Disconnected && disconnected.RequiresPartialAcceptance && disconnected.Operation != early.Operation,
    "Disconnected capture retains a marked prefix and each operation has independent identity");
var failed = await Run(Script(_ => throw new IOException("Controlled start failure")));
Check(failed.Termination == AcquisitionTermination.Failed && failed.Detail == "Controlled start failure" && failed.RawCapture == null,
    "Startup failure remains a failure without fabricated samples");

var time = new ControlledTime();
await using (var session = new AcquisitionSession(new DemoClockSource(1_000, 40, 5, 7, time), new(BatchSamples: 7)))
{
    var job = session.RunAsync();
    bool correctPacing = true;
    for (int i = 0; i < 6; i++)
    {
        var timer = await time.NextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        correctPacing &= timer.Due == TimeSpan.FromMilliseconds(i == 5 ? 5 : 7);
        timer.Fire();
    }
    var result = await job.WaitAsync(TimeSpan.FromSeconds(10)); var signal = result.RawCapture!.Signals.Single();
    Check(correctPacing, "All six Demo batches are paced by their sample duration, including the short final batch");
    Check(result.CanPublish && result.Plan.Kind == AcquisitionSourceKind.Demo && signal.Id == 7
        && signal.Edges.Select(e => e.Sample).SequenceEqual(new long[] {5,10,15,20,25,30,35})
        && result.RawCapture.MeasureAt(7,6) is { WidthSamples:5, PeriodSamples:10, HighSamples:5 },
        "Real Demo acquisition emits raw D7 clock samples with 5-ms pulse and 10-ms period across uneven batches");
    Check(result.RawCapture.SourceDescription.StartsWith("Demo:") && result.RawCapture.Packets.Count == 0,
        "Demo source provenance is explicit and no decoded content is fabricated");
}
var stopTime = new ControlledTime();
await using (var session = new AcquisitionSession(new DemoClockSource(time: stopTime)))
{
    var job = session.RunAsync(); var timer = await stopTime.NextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
    session.RequestStop(); var result = await job.WaitAsync(TimeSpan.FromSeconds(10));
    Check(timer.IsDisposed && result.Termination == AcquisitionTermination.Stopped && result.RetainedSamples == 0 && result.RawCapture == null,
        "Stopping Demo during a pending batch cancels its timer and never invents undelivered data");
}
var i2cTime = new ControlledTime();
await using (var session = new AcquisitionSession(new DemoI2cSource(1000,512,1,31,4,9,i2cTime),new(BatchSamples:127)))
{
    var job=session.RunAsync();
    for(int i=0;i<5;i++) (await i2cTime.NextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10))).Fire();
    var result=await job.WaitAsync(TimeSpan.FromSeconds(10)); var raw=result.RawCapture!;
    Check(result.CanPublish && raw.Signals.Select(s=>s.Id).SequenceEqual(new[]{4,9,31}) && raw.Packets.Count==0
        && raw.DecoderSources!.Length==0,"I2C Demo acquires three physical channels and contains no fabricated decoder result");
    Check(raw.Signals.Single(s=>s.Id==9).Edges.SequenceEqual(new Edge[]{new(8,false),new(216,true)})
        && raw.Signals.Single(s=>s.Id==4).Edges.Contains(new Edge(16,false))
        && raw.Signals.Single(s=>s.Id==4).Edges.Contains(new Edge(94,false))
        && raw.Signals.Single(s=>s.Id==4).Edges.Contains(new Edge(208,true)),
        "Independent I2C/interrupt timeline matches IRQ 8/216, START 16, repeated START 94 and STOP 208");
    Check(raw.MeasureAt(31,21) is { WidthSamples:2,PeriodSamples:4,HighSamples:2 },
        "I2C clock measurement derives a complete 4-sample cycle from actual SCL transitions");
}
Console.WriteLine($"{passed} acquisition checks passed.");
