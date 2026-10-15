// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Threading.Channels;

namespace DSView.Infrastructure;

internal sealed record SampleBatch(long Start, uint[] Samples);

// Bounded ingress. Sample bits use physical channel IDs, not displayed row order.
// Native callbacks use TryWrite; a cooperative Demo producer may wait for capacity.
public sealed class AcquisitionWriter
{
    private readonly object gate = new();
    private readonly AcquisitionPlan plan;
    private readonly Channel<SampleBatch> queue;
    private bool running, closed;
    private long accepted;
    private (AcquisitionTermination Reason, string Detail)? fault;
    internal AcquisitionWriter(AcquisitionPlan plan, AcquisitionLimits limits)
    {
        this.plan = plan; MaximumBatchSamples = limits.BatchSamples;
        queue = Channel.CreateBounded<SampleBatch>(new BoundedChannelOptions(limits.QueueBatches)
        { FullMode = BoundedChannelFullMode.Wait, SingleReader = true, AllowSynchronousContinuations = false });
    }
    public int MaximumBatchSamples { get; }
    internal ChannelReader<SampleBatch> Reader => queue.Reader;
    internal long Accepted { get { lock (gate) return accepted; } }
    internal bool HasStarted { get { lock (gate) return running; } }
    internal (AcquisitionTermination Reason, string Detail)? Fault { get { lock (gate) return fault; } }
    public bool Running()
    {
        lock (gate)
        {
            if (closed) return false;
            if (running) return Fail(AcquisitionTermination.Failed, "Source reported running more than once.");
            running = true; return true;
        }
    }
    public ValueTask<bool> WaitForCapacityAsync(CancellationToken cancellation) => queue.Writer.WaitToWriteAsync(cancellation);
    public bool TryWrite(long startSample, ReadOnlySpan<uint> samples)
    {
        lock (gate)
        {
            if (closed) return false;
            if (!running) return Fail(AcquisitionTermination.Failed, "Samples arrived before confirmed running state.");
            if (startSample != accepted) return Fail(AcquisitionTermination.DataLoss, "Non-contiguous sample interval; retained the preceding prefix.");
            if (samples.Length == 0 || samples.Length > MaximumBatchSamples || samples.Length > plan.SampleLimit - accepted)
                return Fail(AcquisitionTermination.Failed, "Invalid sample batch length or capture limit exceeded.");
            foreach (uint sample in samples)
                if ((sample & ~plan.ChannelMask) != 0) return Fail(AcquisitionTermination.Failed, "Samples contain an unconfigured channel.");
            // Copy before handing off: the producer may reuse its transfer buffer.
            if (!queue.Writer.TryWrite(new(startSample, samples.ToArray())))
                return Fail(AcquisitionTermination.Overflow, "Capture queue is full; retained the preceding prefix.");
            accepted += samples.Length; return true;
        }
    }
    private bool Fail(AcquisitionTermination reason, string detail)
    {
        fault ??= (reason, detail); closed = true; queue.Writer.TryComplete(); return false;
    }
    internal void Reject(AcquisitionTermination reason, string detail) { lock (gate) Fail(reason, detail); }
    internal void Complete() { lock (gate) { closed = true; queue.Writer.TryComplete(); } }
}
