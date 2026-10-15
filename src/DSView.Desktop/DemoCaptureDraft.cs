// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using DSView.Infrastructure;
using DSView.Domain;

namespace DSView.Desktop;

internal sealed record DemoCaptureSettings(int Channel, long Rate, long Samples, int HalfPeriod,
    bool I2c = false, int DataChannel = 1, int InterruptChannel = 8)
{
    public IAcquisitionSource CreateSource() => I2c ? new DemoI2cSource(Rate, Samples, HalfPeriod / 2, Channel, DataChannel, InterruptChannel)
        : new DemoClockSource(Rate, Samples, HalfPeriod, Channel);
    public Capture Configure(Capture raw) => !I2c ? raw : raw with
    { DecoderSources = [new("demo-i2c", [new("scl",Channel),new("sda",DataChannel)]) { Protocol = "i2c" }] };
}
internal sealed class DemoCaptureDraft : ObservableObject
{
    private string channel = "0", rate = "1000000", duration = "1000", frequency = "10000";
    private string scenario = "Clock", dataChannel = "1", interruptChannel = "8";
    public IReadOnlyList<string> Scenarios { get; } = new[] { "Clock", "I²C + Interrupt" };
    public string Scenario
    {
        get => scenario;
        set { if (SetProperty(ref scenario,value)) { OnPropertyChanged(nameof(IsI2c)); OnPropertyChanged(nameof(ChannelLabel)); } }
    }
    public bool IsI2c => Scenario == "I²C + Interrupt";
    public string ChannelLabel => IsI2c ? "SCL · D0–D31" : "Signal · D0–D31";
    public string DataChannel { get => dataChannel; set => SetProperty(ref dataChannel,value); }
    public string InterruptChannel { get => interruptChannel; set => SetProperty(ref interruptChannel,value); }
    public string Channel { get => channel; set => SetProperty(ref channel, value); }
    public string Rate { get => rate; set => SetProperty(ref rate, value); }
    public string Duration { get => duration; set => SetProperty(ref duration, value); }
    public string Frequency { get => frequency; set => SetProperty(ref frequency, value); }
    public string TryCreate(out DemoCaptureSettings? settings)
    {
        settings = null;
        if (!Scenarios.Contains(Scenario)) return "Choose a supported Demo scenario.";
        if (!int.TryParse(Channel, NumberStyles.None, CultureInfo.InvariantCulture, out int id) || id is < 0 or > 31) return "Signal must be D0–D31. Enter its number.";
        if (!long.TryParse(Rate, NumberStyles.None, CultureInfo.InvariantCulture, out long hz) || hz is < 1 or > 1_000_000_000) return "Enter a sample rate from 1 to 1,000,000,000 Hz.";
        if (!decimal.TryParse(Duration, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal ms) || ms <= 0 || ms > 64_000_000_000m)
            return "Enter a positive duration in milliseconds.";
        decimal count = hz * ms / 1000;
        if (count is < 10 or > 64_000_000 || count != decimal.Truncate(count)) return "Duration must give 10–64,000,000 whole samples.";
        if (!long.TryParse(Frequency, NumberStyles.None, CultureInfo.InvariantCulture, out long clock) || clock < 1 || clock > hz / 2 || hz % (2 * clock) != 0)
            return "Clock frequency must divide the sample rate into whole high and low intervals.";
        int half = (int)(hz / (2 * clock)), data = 1, interrupt = 8;
        if (IsI2c)
        {
            if (half % 2 != 0) return "I²C needs four whole sample intervals per clock cycle.";
            if (!int.TryParse(DataChannel,NumberStyles.None,CultureInfo.InvariantCulture,out data) || data is < 0 or > 31
                || !int.TryParse(InterruptChannel,NumberStyles.None,CultureInfo.InvariantCulture,out interrupt) || interrupt is < 0 or > 31
                || id == data || id == interrupt || data == interrupt) return "SCL, SDA and Interrupt must use three distinct signals, D0–D31.";
        }
        settings = new(id, hz, (long)count, half, IsI2c, data, interrupt); return "";
    }
}
