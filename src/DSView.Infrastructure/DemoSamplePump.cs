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
        while (start < plan.SampleLimit)
        {
            cancellation.ThrowIfCancellationRequested();
            int count = (int)Math.Min(samples.Length, plan.SampleLimit - start);
            for (int i = 0; i < count; i++) samples[i] = sampleAt(start + i);
            // Delivery may run slower under load; timing always follows sample positions.
            await Task.Delay(TimeSpan.FromSeconds((double)count / plan.SampleRate), time, cancellation).ConfigureAwait(false);
            if (!await output.WaitForCapacityAsync(cancellation).ConfigureAwait(false) || !output.TryWrite(start, samples.AsSpan(0, count)))
                return AcquisitionTermination.Overflow;
            start += count;
        }
        return AcquisitionTermination.Completed;
    }
}
