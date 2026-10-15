// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using DSView.Infrastructure;
using DSView.Domain;

namespace DSView.Desktop;

internal enum DemoScenario { Clock, I2c, Uart, Spi }
internal sealed record DemoCaptureSettings(int Channel, long Rate, long Samples, int Frequency,
    DemoScenario Scenario = DemoScenario.Clock, int DataChannel = 1, int InterruptChannel = 8, int MisoChannel = 2, int ChipSelectChannel = 3)
{
    public IAcquisitionSource CreateSource() => Scenario switch
    {
        DemoScenario.Clock => new DemoClockSource(Rate, Samples, (int)(Rate / Frequency / 2), Channel),
        DemoScenario.I2c => new DemoI2cSource(Rate, Samples, (int)(Rate / Frequency / 4), Channel, DataChannel, InterruptChannel),
        DemoScenario.Uart => new DemoUartSource(Rate, Samples, Frequency, Channel),
        DemoScenario.Spi => new DemoSpiSource(Rate, Samples, (int)(Rate / Frequency / 2), Channel, DataChannel, MisoChannel, ChipSelectChannel),
        _ => throw new InvalidOperationException("Unsupported Demo scenario.")
    };
    public Capture Configure(Capture raw) => Scenario switch
    {
        DemoScenario.I2c => raw with { DecoderSources = [new("demo-i2c", [new("scl",Channel),new("sda",DataChannel)]) { Protocol = "i2c" }] },
        DemoScenario.Uart => raw with { DecoderSources = [new("demo-uart", [new("data",Channel)]) { Protocol = "uart", Uart = new(Frequency,8,"none",1,"lsb-first",false) }] },
        DemoScenario.Spi => raw with { DecoderSources = [new("demo-spi", [new("clk",Channel),new("mosi",DataChannel),new("miso",MisoChannel),new("cs",ChipSelectChannel)]) { Protocol = "spi", Spi = new(0,"msb-first",8,"active-low") }] },
        _ => raw
    };
}
internal sealed class DemoCaptureDraft : ObservableObject
{
    private string channel = "0", rate = "1000000", duration = "1000", frequency = "10000";
    private string scenario = "Clock", dataChannel = "1", interruptChannel = "8", misoChannel = "2", chipSelectChannel = "3";
    public IReadOnlyList<string> Scenarios { get; } = new[] { "Clock", "I²C + Interrupt", "UART · 8N1", "SPI · Mode 0" };
    public string Scenario
    {
        get => scenario;
        set
        {
            if (SetProperty(ref scenario,value))
                foreach (string property in new[] { nameof(IsI2c), nameof(IsSpi), nameof(HasDataChannel), nameof(DataChannelLabel), nameof(ChannelLabel), nameof(FrequencyLabel) })
                    OnPropertyChanged(property);
        }
    }
    public bool IsI2c => Scenario == "I²C + Interrupt";
    public bool IsSpi => Scenario == "SPI · Mode 0";
    public bool HasDataChannel => IsI2c || IsSpi;
    public string DataChannelLabel => IsSpi ? "MOSI · D0–D31" : "SDA · D0–D31";
    private bool IsUart => Scenario == "UART · 8N1";
    public string ChannelLabel => IsI2c ? "SCL · D0–D31" : IsSpi ? "CLK · D0–D31" : IsUart ? "TX · D0–D31" : "Signal · D0–D31";
    public string FrequencyLabel => IsUart ? "Baud rate · bit/s" : "Clock frequency · Hz";
    public string DataChannel { get => dataChannel; set => SetProperty(ref dataChannel,value); }
    public string InterruptChannel { get => interruptChannel; set => SetProperty(ref interruptChannel,value); }
    public string MisoChannel { get => misoChannel; set => SetProperty(ref misoChannel,value); }
    public string ChipSelectChannel { get => chipSelectChannel; set => SetProperty(ref chipSelectChannel,value); }
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
        if (!int.TryParse(Frequency, NumberStyles.None, CultureInfo.InvariantCulture, out int clock) || clock < 1)
            return IsUart ? "Enter a positive baud rate." : "Enter a positive clock frequency.";
        if (IsUart && (clock > hz / 4 || hz % clock != 0))
            return "UART needs at least four whole samples per bit. For 115200 baud, try 1152000 samples/s.";
        if (!IsUart && (clock > hz / 2 || hz % (2L * clock) != 0))
            return "Clock frequency must divide the sample rate into whole high and low intervals.";
        int data = 1, interrupt = 8, miso = 2, cs = 3;
        if (IsI2c)
        {
            if (hz % (4L * clock) != 0) return "I²C needs four whole sample intervals per clock cycle.";
            if (!int.TryParse(DataChannel,NumberStyles.None,CultureInfo.InvariantCulture,out data) || data is < 0 or > 31
                || !int.TryParse(InterruptChannel,NumberStyles.None,CultureInfo.InvariantCulture,out interrupt) || interrupt is < 0 or > 31
                || id == data || id == interrupt || data == interrupt) return "SCL, SDA and Interrupt must use three distinct signals, D0–D31.";
        }
        if (IsSpi && (!int.TryParse(DataChannel,NumberStyles.None,CultureInfo.InvariantCulture,out data) || data is < 0 or > 31
            || !int.TryParse(MisoChannel,NumberStyles.None,CultureInfo.InvariantCulture,out miso) || miso is < 0 or > 31
            || !int.TryParse(ChipSelectChannel,NumberStyles.None,CultureInfo.InvariantCulture,out cs) || cs is < 0 or > 31
            || new[] {id,data,miso,cs}.Distinct().Count()!=4)) return "CLK, MOSI, MISO and CS must use four distinct signals, D0–D31.";
        settings = new(id, hz, (long)count, clock, IsI2c ? DemoScenario.I2c : IsUart ? DemoScenario.Uart : IsSpi ? DemoScenario.Spi : DemoScenario.Clock, data, interrupt, miso, cs); return "";
    }
}
