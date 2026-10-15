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
    public Task<AcquisitionTermination> AcquireAsync(AcquisitionWriter output, CancellationToken stop, CancellationToken abort) =>
        DemoSamplePump.RunAsync(Plan, sample => (sample / halfPeriodSamples & 1) == 0 ? 0 : Plan.ChannelMask, time, output, stop, abort);
}
