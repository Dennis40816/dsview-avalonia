// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
namespace DSView.Infrastructure;

// Public full-duplex, mode 0, 8-bit, MSB-first stimulus. No decoder output.
public sealed class DemoSpiSource : IAcquisitionSource
{
    private static readonly byte[] Transmit = [0x9F, 0x00, 0xA5], Receive = [0x00, 0xEF, 0x17];
    private readonly int halfSamples;
    private readonly uint clockMask, mosiMask, misoMask, csMask;
    private readonly TimeProvider time;
    public DemoSpiSource(long sampleRate = 1_000_000, long sampleLimit = 1_000_000,
        int halfSamples = 5, int clock = 0, int mosi = 1, int miso = 2, int chipSelect = 3, TimeProvider? time = null)
    {
        if (halfSamples < 1) throw new ArgumentOutOfRangeException(nameof(halfSamples));
        Plan = new(AcquisitionSourceKind.Demo,"SPI · Mode 0",sampleRate,sampleLimit,
            [new(clock,"CLK"),new(mosi,"MOSI"),new(miso,"MISO"),new(chipSelect,"CS_N")]);
        this.halfSamples=halfSamples; this.time=time??TimeProvider.System;
        clockMask=1u<<clock; mosiMask=1u<<mosi; misoMask=1u<<miso; csMask=1u<<chipSelect;
    }
    public AcquisitionPlan Plan { get; }
    private uint SampleAt(long sample)
    {
        int slot=(int)(sample/halfSamples%128);
        if (slot<8 || slot>=60) return csMask;
        if (slot<10) return 0;
        int bit=Math.Min((slot-10)/2,23), shift=7-bit%8;
        return (slot<58 && (slot&1)!=0 ? clockMask : 0)
            | ((Transmit[bit/8]&(1<<shift))!=0 ? mosiMask : 0)
            | ((Receive[bit/8]&(1<<shift))!=0 ? misoMask : 0);
    }
    public Task<AcquisitionTermination> AcquireAsync(AcquisitionWriter output, CancellationToken stop, CancellationToken abort) =>
        DemoSamplePump.RunAsync(Plan,SampleAt,time,output,stop,abort);
}
