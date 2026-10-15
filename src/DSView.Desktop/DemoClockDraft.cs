// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using DSView.Infrastructure;

namespace DSView.Desktop;

internal sealed record DemoClockSettings(int Channel, long Rate, long Samples, int HalfPeriod)
{
    public IAcquisitionSource CreateSource() => new DemoClockSource(Rate, Samples, HalfPeriod, Channel);
}
internal sealed class DemoClockDraft : ObservableObject
{
    private string channel = "0", rate = "1000000", duration = "1000", frequency = "10000";
    public string Channel { get => channel; set => SetProperty(ref channel, value); }
    public string Rate { get => rate; set => SetProperty(ref rate, value); }
    public string Duration { get => duration; set => SetProperty(ref duration, value); }
    public string Frequency { get => frequency; set => SetProperty(ref frequency, value); }
    public string TryCreate(out DemoClockSettings? settings)
    {
        settings = null;
        if (!int.TryParse(Channel, NumberStyles.None, CultureInfo.InvariantCulture, out int id) || id is < 0 or > 31) return "Signal must be D0–D31. Enter its number.";
        if (!long.TryParse(Rate, NumberStyles.None, CultureInfo.InvariantCulture, out long hz) || hz is < 1 or > 1_000_000_000) return "Enter a sample rate from 1 to 1,000,000,000 Hz.";
        if (!decimal.TryParse(Duration, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal ms) || ms <= 0 || ms > 64_000_000_000m)
            return "Enter a positive duration in milliseconds.";
        decimal count = hz * ms / 1000;
        if (count is < 10 or > 64_000_000 || count != decimal.Truncate(count)) return "Duration must give 10–64,000,000 whole samples.";
        if (!long.TryParse(Frequency, NumberStyles.None, CultureInfo.InvariantCulture, out long clock) || clock < 1 || clock > hz / 2 || hz % (2 * clock) != 0)
            return "Clock frequency must divide the sample rate into whole high and low intervals.";
        settings = new(id, hz, (long)count, (int)(hz / (2 * clock))); return "";
    }
}
