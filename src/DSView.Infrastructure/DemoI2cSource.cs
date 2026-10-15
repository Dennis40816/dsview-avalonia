// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
namespace DSView.Infrastructure;

// Public synthetic register read. This encodes bus levels, not decoder packets.
// Quarter-cycle units keep data changes away from SCL rising edges. Repeats include
// a quiet interval; captures may end partway through a transaction.
public sealed class DemoI2cSource : IAcquisitionSource
{
    private readonly uint[] levels;
    private readonly int quarterSamples;
    private readonly TimeProvider time;
    public DemoI2cSource(long sampleRate = 2_000_000, long sampleLimit = 2_000_000,
        int quarterSamples = 5, int scl = 0, int sda = 1, int interrupt = 8, TimeProvider? time = null)
    {
        if (quarterSamples < 1) throw new ArgumentOutOfRangeException(nameof(quarterSamples));
        Plan = new(AcquisitionSourceKind.Demo, "I²C + Interrupt", sampleRate, sampleLimit,
            [new(scl,"SCL"),new(sda,"SDA"),new(interrupt,"INT_N")]);
        this.quarterSamples = quarterSamples; this.time = time ?? TimeProvider.System;
        levels = CreateLevels(1u << scl, 1u << sda, 1u << interrupt);
    }
    public AcquisitionPlan Plan { get; }
    private static uint[] CreateLevels(uint scl, uint sda, uint interrupt)
    {
        var result = new List<uint>(512);
        void Hold(bool clock, bool data, int quarters)
        {
            for (int i = 0; i < quarters; i++)
                result.Add((clock ? scl : 0) | (data ? sda : 0) | (result.Count < 8 || result.Count >= 216 ? interrupt : 0));
        }
        void Byte(byte value, bool acknowledge)
        {
            for (int bit = 7; bit >= 0; bit--)
            { bool high = (value & (1 << bit)) != 0; Hold(false,high,2); Hold(true,high,2); }
            Hold(false,!acknowledge,2); Hold(true,!acknowledge,2);
        }
        Hold(true,true,16); Hold(true,false,2); // START at quarter 16.
        Byte(0x54,true); Byte(0x10,true); // Address 0x2A write; register 0x10.
        Hold(false,true,2); Hold(true,true,2); Hold(true,false,2); // Repeated START at 94.
        Byte(0x55,true); Byte(0x5A,true); Byte(0xC3,false); // Read; final byte NACK.
        Hold(false,false,2); Hold(true,false,2); Hold(true,true,2); // STOP at 208.
        Hold(true,true,512-result.Count);
        return result.ToArray();
    }
    public Task<AcquisitionTermination> AcquireAsync(AcquisitionWriter output, CancellationToken stop, CancellationToken abort) =>
        DemoSamplePump.RunAsync(Plan, sample => levels[(int)(sample / quarterSamples % levels.Length)], time, output, stop, abort);
}
