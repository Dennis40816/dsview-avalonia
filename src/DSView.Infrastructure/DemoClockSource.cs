// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
namespace DSView.Infrastructure;

// A first acquisition-mode scenario, separate from precomputed offline examples.
// The time provider permits deterministic pacing tests; no wall-clock sleeps in tests.
public sealed class DemoClockSource : IAcquisitionSource
{
    private readonly TimeProvider time;
    private readonly int halfPeriodSamples;
    public DemoClockSource(long sampleRate = 1_000_000, long sampleLimit = 100_000,
        int halfPeriodSamples = 50, int channel = 0, TimeProvider? time = null)
    {
        if (halfPeriodSamples < 1) throw new ArgumentOutOfRangeException(nameof(halfPeriodSamples));
        Plan = new(AcquisitionSourceKind.Demo, "Clock", sampleRate, sampleLimit, [new(channel, "CLOCK")]);
        this.halfPeriodSamples = halfPeriodSamples; this.time = time ?? TimeProvider.System;
    }
    public AcquisitionPlan Plan { get; }
    public async Task<AcquisitionTermination> AcquireAsync(AcquisitionWriter output, CancellationToken stop, CancellationToken abort)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stop, abort);
        var cancellation = linked.Token;
        if (!output.Running()) return AcquisitionTermination.Failed;
        uint[] samples = new uint[output.MaximumBatchSamples];
        long start = 0;
        while (start < Plan.SampleLimit)
        {
            cancellation.ThrowIfCancellationRequested();
            int count = (int)Math.Min(samples.Length, Plan.SampleLimit - start);
            for (int i = 0; i < count; i++) samples[i] = ((start + i) / halfPeriodSamples & 1) == 0 ? 0 : Plan.ChannelMask;
            // Acquisition time elapses before the batch is delivered. The demo may
            // run slower under load, but its sample timing never depends on wall time.
            await Task.Delay(TimeSpan.FromSeconds((double)count / Plan.SampleRate), time, cancellation).ConfigureAwait(false);
            if (!await output.WaitForCapacityAsync(cancellation).ConfigureAwait(false) || !output.TryWrite(start, samples.AsSpan(0, count)))
                return AcquisitionTermination.Overflow;
            start += count;
        }
        return AcquisitionTermination.Completed;
    }
}
