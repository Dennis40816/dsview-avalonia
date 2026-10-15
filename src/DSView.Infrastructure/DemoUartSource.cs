// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
namespace DSView.Infrastructure;

// Repeated public 8N1 stimulus, LSB first. Raw levels only, no decoded frames.
public sealed class DemoUartSource : IAcquisitionSource
{
    private static readonly byte[] Data = [0x55, 0xA5, 0x00, 0xFF];
    private readonly int bitSamples;
    private readonly TimeProvider time;
    public DemoUartSource(long sampleRate = 1_152_000, long sampleLimit = 1_152_000,
        int baudRate = 115_200, int channel = 0, TimeProvider? time = null)
    {
        if (baudRate <= 0 || sampleRate % baudRate != 0 || sampleRate / baudRate < 4)
            throw new ArgumentException("UART Demo needs at least four whole samples per bit.", nameof(baudRate));
        Plan = new(AcquisitionSourceKind.Demo, "UART · 8N1", sampleRate, sampleLimit, [new(channel,"TX")]);
        bitSamples = (int)(sampleRate / baudRate); this.time = time ?? TimeProvider.System;
    }
    public AcquisitionPlan Plan { get; }
    private uint SampleAt(long sample)
    {
        int slot = (int)(sample / bitSamples % 64);
        if (slot < 8 || slot >= 48) return Plan.ChannelMask;
        int frameBit = (slot - 8) % 10;
        bool high = frameBit == 9 || frameBit > 0 && ((Data[(slot - 8) / 10] >> (frameBit - 1)) & 1) != 0;
        return high ? Plan.ChannelMask : 0;
    }
    public Task<AcquisitionTermination> AcquireAsync(AcquisitionWriter output, CancellationToken stop, CancellationToken abort) =>
        DemoSamplePump.RunAsync(Plan, SampleAt, time, output, stop, abort);
}
