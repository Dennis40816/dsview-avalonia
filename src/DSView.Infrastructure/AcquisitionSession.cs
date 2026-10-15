// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
namespace DSView.Infrastructure;

// One session is one operation. The UI must match Operation before publishing and
// await disposal before replacing its source. Stop retains a prefix; abort discards.
public sealed class AcquisitionSession : IAsyncDisposable
{
    private readonly object gate = new();
    private readonly IAcquisitionSource source;
    private readonly AcquisitionWriter writer;
    private readonly AcquisitionBuffer buffer;
    private readonly CancellationTokenSource stop = new(), abort = new();
    private Task<AcquisitionResult>? task;
    private long retained;
    private bool disposed;
    public AcquisitionSession(IAcquisitionSource source, AcquisitionLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(source); this.source = source;
        Plan = source.Plan ?? throw new ArgumentException("A verified acquisition plan is required.");
        limits ??= new(); limits.Validate();
        writer = new(Plan, limits); buffer = new(Plan, limits.Transitions);
    }
    public Guid Operation { get; } = Guid.NewGuid();
    public AcquisitionPlan Plan { get; }
    public AcquisitionProgress Progress
    {
        get
        {
            lock (gate) return new(Operation, task?.IsCompleted == true ? AcquisitionPhase.Finished :
                stop.IsCancellationRequested || abort.IsCancellationRequested ? AcquisitionPhase.Stopping :
                writer.HasStarted ? AcquisitionPhase.Running : AcquisitionPhase.Preparing, writer.Accepted, Interlocked.Read(ref retained));
        }
    }
    public Task<AcquisitionResult> RunAsync()
    {
        lock (gate) { ObjectDisposedException.ThrowIf(disposed, this); return task ??= Task.Run(RunCoreAsync); }
    }
    public void RequestStop() { lock (gate) { if (!disposed && task?.IsCompleted != true) stop.Cancel(); } }
    public void Cancel() { lock (gate) { if (!disposed && task?.IsCompleted != true) { writer.Complete(); abort.Cancel(); } } }
    private async Task<AcquisitionResult> RunCoreAsync()
    {
        var end = AcquisitionTermination.Failed; string detail = "";
        var producer = Task.Run(async () =>
        {
            try
            {
                abort.Token.ThrowIfCancellationRequested(); stop.Token.ThrowIfCancellationRequested();
                end = await source.AcquireAsync(writer, stop.Token, abort.Token).ConfigureAwait(false);
                if (!Enum.IsDefined(end)) { end = AcquisitionTermination.Failed; detail = "Source returned an unknown termination."; }
            }
            catch (OperationCanceledException) when (abort.IsCancellationRequested) { end = AcquisitionTermination.Cancelled; }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { end = AcquisitionTermination.Stopped; }
            catch (Exception ex) { end = AcquisitionTermination.Failed; detail = ex.Message[..Math.Min(ex.Message.Length, 1024)]; }
            finally { writer.Complete(); }
        });
        bool retain = true;
        try
        {
            await foreach (var batch in writer.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                if (!retain || abort.IsCancellationRequested) continue;
                if (!buffer.Append(batch))
                {
                    retain = false;
                    writer.Reject(AcquisitionTermination.Overflow, "Transition storage limit reached; retained the preceding prefix.");
                    stop.Cancel();
                }
                Interlocked.Exchange(ref retained, buffer.Count);
            }
        }
        catch (Exception ex)
        {
            writer.Reject(AcquisitionTermination.Failed, "Sample processing failed: " + ex.Message);
            stop.Cancel();
        }
        await producer.ConfigureAwait(false); // No terminal result while source work is still alive.
        if (writer.Fault is { } fault) { end = fault.Reason; detail = fault.Detail; }
        if (abort.IsCancellationRequested) { end = AcquisitionTermination.Cancelled; detail = "Acquisition discarded."; }
        else if (end == AcquisitionTermination.Completed && (!writer.HasStarted || buffer.Count != Plan.SampleLimit))
        { end = AcquisitionTermination.DataLoss; detail = "Source ended before the configured sample count."; }
        long retainedCount = end == AcquisitionTermination.Cancelled ? 0 : buffer.Count;
        Interlocked.Exchange(ref retained, retainedCount);
        var raw = end == AcquisitionTermination.Cancelled ? null : buffer.Snapshot(end);
        buffer.Release();
        return new(Operation, Plan, end, writer.Accepted, retainedCount, raw, detail);
    }
    public async ValueTask DisposeAsync()
    {
        Task<AcquisitionResult>? running;
        lock (gate) { if (disposed) return; disposed = true; writer.Complete(); abort.Cancel(); running = task; }
        if (running != null) await running.ConfigureAwait(false);
        stop.Dispose(); abort.Dispose();
    }
}
