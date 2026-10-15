// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
namespace DSView.Infrastructure;

internal static class DemoSamplePump
{
    public static async Task<AcquisitionTermination> RunAsync(AcquisitionPlan plan, Func<long,uint> sampleAt,
        TimeProvider time, AcquisitionWriter output, CancellationToken stop, CancellationToken abort)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stop, abort);
        var cancellation = linked.Token;
        if (!output.Running()) return AcquisitionTermination.Failed;
        uint[] samples = new uint[output.MaximumBatchSamples]; long start = 0;
        long began = time.GetTimestamp();
        while (start < plan.SampleLimit)
        {
            cancellation.ThrowIfCancellationRequested();
            int count = (int)Math.Min(samples.Length, plan.SampleLimit - start);
            for (int i = 0; i < count; i++) samples[i] = sampleAt(start + i);
            // Schedule against one monotonic origin. Per-batch delays accumulate OS timer
            // lateness and processing time. Catch up through the same bounded queue, never
            // by discarding samples or changing their acquisition coordinates.
            var remaining = TimeSpan.FromSeconds((double)(start + count) / plan.SampleRate) - time.GetElapsedTime(began);
            if (remaining > TimeSpan.Zero)
                await Task.Delay(remaining, time, cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            if (!await output.WaitForCapacityAsync(cancellation).ConfigureAwait(false) || !output.TryWrite(start, samples.AsSpan(0, count)))
                return AcquisitionTermination.Overflow;
            start += count;
        }
        return AcquisitionTermination.Completed;
    }
}
