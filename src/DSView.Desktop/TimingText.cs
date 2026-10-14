// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using DSView.Domain;

namespace DSView.Desktop;

public static class TimingText
{
    private static string Number(double value)
    {
        string text = value.ToString("0.###", CultureInfo.InvariantCulture);
        return text == "-0" ? "0" : text;
    }
    public static string Duration(double seconds) => Math.Abs(seconds) < 1e-6 && seconds != 0
        ? Number(seconds * 1e9) + " ns" : Math.Abs(seconds) < 1e-3
        ? Number(seconds * 1e6) + " µs" : Math.Abs(seconds) < 1
        ? Number(seconds * 1e3) + " ms" : Number(seconds) + " s";
    public static string Frequency(double hz) => hz >= 1e6 ? Number(hz / 1e6) + " MHz" : hz >= 1e3 ? Number(hz / 1e3) + " kHz" : Number(hz) + " Hz";
    public static string Packet(Packet packet, long rate) => $"{packet.Id,-8} {packet.Decoder.ToUpperInvariant(),-5}   {Duration(packet.Span.Start / (double)rate),10}   {packet.Hex,-22}   {packet.Error ?? packet.Title}";
    public static string I2cStatus(Packet packet)
    {
        if (packet.I2c is not { } bus || bus.Acknowledged.Count != packet.Bytes.Length + 1) return "I²C · Inspect ACK / NACK evidence";
        if (bus.Acknowledged[0] == false) return "I²C · Address NACK";
        if (bus.Acknowledged.All(a => a == true)) return "I²C · Address + data ACK";
        if (bus.Read && packet.Bytes.Length > 0 && bus.Acknowledged.Take(bus.Acknowledged.Count - 1).All(a => a == true) && bus.Acknowledged[^1] == false) return "I²C · Read ends with NACK";
        return "I²C · Inspect ACK / NACK evidence";
    }
    public static string Local(LocalMeasurement value, long rate)
    {
        if (value.WidthSamples is not long width) return value.Reason ?? "Measurement unavailable";
        var text = $"{(value.High ? "High" : "Low")} {Duration(width / (double)rate)}";
        return value.PeriodSamples is long period ? text + $"   ·   T {Duration(period / (double)rate)}   ·   {Frequency(rate / (double)period)}" : text + "   ·   T/f unavailable";
    }
}
