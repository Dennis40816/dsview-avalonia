// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using DSView.Domain;

namespace DSView.Infrastructure;

// Native input planes are SCL then SDA. Physical IDs are never renumbered.
internal static class I2cSampleInput
{
    public static byte[] Pack(Capture raw, I2cChannelMap channels, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (raw.SampleRate <= 0 || raw.SampleCount is < 10 or > 64_000_000 || raw.Gaps.Count != 0)
            throw new InvalidDataException("I²C decoding currently requires a finite, continuous capture.");
        var sources = channels.Resolve(raw);
        return PackPlanes(raw, sources, cancellation);
    }
    internal static byte[] PackPlanes(Capture raw, Signal?[] sources, CancellationToken cancellation)
    {
        int size = checked((int)((raw.SampleCount + 7) / 8));
        var bytes = new byte[checked(size * sources.Length)];
        for (int plane = 0; plane < sources.Length; plane++)
        {
            cancellation.ThrowIfCancellationRequested();
            var signal = sources[plane]; if (signal == null) continue; // Unmapped optional native plane.
            long previous = 0; bool high = signal.Initial;
            foreach (var edge in signal.Edges)
            {
                cancellation.ThrowIfCancellationRequested();
                if (edge.Sample <= previous || edge.Sample >= raw.SampleCount || edge.High == high)
                    throw new InvalidDataException("Raw transitions must be ordered, alternating and inside the capture.");
                if (high) Fill(previous, edge.Sample, plane * size);
                previous = edge.Sample; high = edge.High;
            }
            if (high) Fill(previous, raw.SampleCount, plane * size);
        }
        cancellation.ThrowIfCancellationRequested();
        return bytes;
        void Fill(long first, long end, int offset)
        {
            while (first < end && first % 8 != 0) { bytes[offset + (int)(first / 8)] |= (byte)(1 << (int)(first % 8)); first++; }
            long whole = (end - first) / 8;
            Array.Fill(bytes, (byte)255, offset + (int)(first / 8), (int)whole); first += whole * 8;
            while (first < end) { bytes[offset + (int)(first / 8)] |= (byte)(1 << (int)(first % 8)); first++; }
        }
    }
}
