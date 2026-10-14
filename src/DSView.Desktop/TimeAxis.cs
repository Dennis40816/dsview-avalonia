// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;

namespace DSView.Desktop;

public sealed record TimeTick(double Sample, string Label);
public sealed record TimeAxis(string Unit, string? OriginText, string RangeText, IReadOnlyList<TimeTick> Ticks)
{
    // Display coordinates only. Source selection and measurement never parse ruler labels.
    private sealed record Scale(decimal Start, decimal End, decimal Origin, decimal Factor, string Unit, decimal Resolution, string? OriginText);
    private static (decimal Factor, string Unit) Units(decimal seconds) => Math.Abs(seconds) < .000001m
        ? (1_000_000_000m, "ns") : Math.Abs(seconds) < .001m ? (1_000_000m, "µs") : Math.Abs(seconds) < 1 ? (1000m, "ms") : (1m, "s");
    private static int Precision(decimal step) => Math.Clamp((int)Math.Ceiling(-Math.Log10((double)step)), 0, 18);
    private static decimal SampleValue(double sample)
    {
        // A direct double-to-decimal cast retains only about 15 significant digits.
        // Preserve the integer samples already represented by the viewport first.
        ulong whole = checked((ulong)Math.Truncate(sample));
        return whole + (decimal)(sample - whole);
    }
    private static string Number(decimal value, int decimals)
    {
        value = decimal.Round(value, decimals);
        return value == 0 ? "0" : value.ToString(decimals == 0 ? "0" : "0." + new string('#', decimals), CultureInfo.InvariantCulture);
    }
    private static Scale Coordinates(double start, double end, long rate)
    {
        if (!double.IsFinite(start) || !double.IsFinite(end) || start < 0 || end < start || rate <= 0) throw new ArgumentOutOfRangeException(nameof(start));
        decimal first = SampleValue(start), last = SampleValue(end);
        decimal span = (last - first) / rate;
        var (factor, unit) = Units(Math.Max(span, 1m / rate));
        decimal from = first / rate * factor, to = last / rate * factor;
        decimal origin = from >= 1000 && from > Math.Max(to - from, factor / rate) * 1000 ? decimal.Floor(from / 1000) * 1000 : 0;
        string? originText = null;
        if (origin != 0)
        {
            var absolute = Units(origin / factor);
            originText = Number(origin / factor * absolute.Factor, Math.Min(18, Precision(absolute.Factor / rate) + 1)) + " " + absolute.Unit;
        }
        return new(from - origin, to - origin, origin, factor, unit, factor / rate, originText);
    }
    private static string Range(Scale scale)
    {
        int decimals = Math.Min(18, Precision(scale.Resolution) + 1);
        string range = Number(scale.Start, decimals) + "–" + Number(scale.End, decimals) + " " + scale.Unit;
        return scale.OriginText == null ? range : scale.OriginText + " + (" + range + ")";
    }
    public static string FormatRange(double start, double end, long rate) => Range(Coordinates(start, end, rate));
    public static string Microseconds(long sample, long rate) => Number((decimal)sample * 1_000_000 / rate, Math.Min(18, Precision(1_000_000m / rate) + 1));
    public static TimeAxis Create(double start, double end, long rate, double width)
    {
        if (end <= start || !double.IsFinite(width) || width <= 0) throw new ArgumentOutOfRangeException(nameof(end));
        var scale = Coordinates(start, end, rate);
        decimal rough = Math.Max(scale.Resolution, (scale.End - scale.Start) / (decimal)Math.Clamp(width / 110, 2, 40));
        decimal power = (decimal)Math.Pow(10, Math.Floor(Math.Log10((double)rough)));
        decimal step = new[] { 1m, 2m, 5m, 10m }.First(n => n * power >= rough) * power;
        decimal first = decimal.Ceiling(scale.Start / step);
        var ticks = new List<TimeTick>();
        for (int i = 0; i < 64; i++)
        {
            decimal value = (first + i) * step;
            if (value > scale.End) break;
            decimal coordinate = (value + scale.Origin) / scale.Factor * rate;
            ulong whole = checked((ulong)decimal.Truncate(coordinate));
            double sample = whole + (double)(coordinate - whole);
            if (sample >= start && sample <= end) ticks.Add(new(sample, Number(value, Precision(step))));
        }
        return new(scale.Unit, scale.OriginText, Range(scale), ticks);
    }
}
