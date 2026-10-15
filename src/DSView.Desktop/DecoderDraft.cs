// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using DSView.Domain;

namespace DSView.Desktop;

internal sealed record DecoderChannel(int Id, string Label, bool Available = true);
internal sealed class DecoderRoleDraft : ObservableObject
{
    private readonly Func<bool> canEdit;
    private DecoderChannel? selected;
    public DecoderRoleDraft(string role, bool optional, IReadOnlyList<DecoderChannel> channels, int? saved, Func<bool> canEdit)
    {
        Role = role; this.canEdit = canEdit;
        var choices = channels.ToList();
        if (optional) choices.Insert(0, new(-1, "None"));
        if (saved is int id && !choices.Any(c => c.Id == id)) choices.Add(new(id, $"D{id} · unavailable", false));
        Channels = choices;
        selected = saved is int channel ? choices.First(c => c.Id == channel) : optional ? choices[0] : null;
    }
    public string Role { get; }
    public string Label => Role.ToUpperInvariant();
    public IReadOnlyList<DecoderChannel> Channels { get; }
    public DecoderChannel? Selected { get => selected; set { if (canEdit() && SetProperty(ref selected, value)) Changed?.Invoke(); } }
    public event Action? Changed;
}

// A local, discardable settings draft. Only a successful decode publishes it.
internal sealed class DecoderDraft : ObservableObject
{
    private readonly Func<bool> canEdit;
    private readonly Capture? capture;
    private readonly DecoderSource? existing;
    private string protocol, baud = "115200", bits = "8", order = "msb-first", parity = "none", polarity = "active-low";
    private int mode;
    private bool inverted;
    public DecoderDraft(Capture? capture, DecoderSource? existing, Func<bool> canEdit)
    {
        this.capture = capture; this.existing = existing; this.canEdit = canEdit;
        Channels = capture?.Signals.OrderBy(s => s.Id).Select(s => new DecoderChannel(s.Id, $"D{s.Id} · {s.Name}")).ToArray() ?? [];
        protocol = existing?.EffectiveProtocol switch { "spi" => "SPI", "uart" => "UART", _ => "I²C" };
        Reset(existing);
    }
    public event Action? Changed;
    public IReadOnlyList<DecoderChannel> Channels { get; }
    public IReadOnlyList<DecoderRoleDraft> Roles { get; private set; } = [];
    public string[] Protocols { get; } = ["I²C", "SPI", "UART"];
    public int[] Modes { get; } = [0, 1, 2, 3];
    public string[] Orders { get; } = ["msb-first", "lsb-first"];
    public string[] Parities { get; } = ["none", "odd", "even", "zero", "one"];
    public string[] Polarities { get; } = ["active-low", "active-high"];
    public bool CanChooseProtocol => existing == null;
    public string Protocol { get => protocol; set { if (canEdit() && CanChooseProtocol && Protocols.Contains(value) && SetProperty(ref protocol, value)) { Reset(null); Changed?.Invoke(); } } }
    public bool IsSpi => Protocol == "SPI";
    public bool IsUart => Protocol == "UART";
    public bool IsSerial => IsSpi || IsUart;
    public bool HasUnknownSettings => existing != null && (IsSpi && existing.Spi == null || IsUart && existing.Uart == null);
    public string Baud { get => baud; set => Edit(ref baud, value); }
    public string Bits { get => bits; set => Edit(ref bits, value); }
    public string Order { get => order; set => Edit(ref order, value); }
    public string Parity { get => parity; set => Edit(ref parity, value); }
    public string Polarity { get => polarity; set => Edit(ref polarity, value); }
    public int Mode { get => mode; set => Edit(ref mode, value); }
    public bool Inverted { get => inverted; set => Edit(ref inverted, value); }
    private void Edit<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    { if (canEdit() && SetProperty(ref field, value, name)) Changed?.Invoke(); }
    private string Code => IsSpi ? "spi" : IsUart ? "uart" : "i2c";
    private void Reset(DecoderSource? saved)
    {
        foreach (var role in Roles) role.Changed -= OnChanged;
        string[] names = IsSpi ? ["clk", "mosi", "miso", "cs"] : IsUart ? ["data"] : ["scl", "sda"];
        Roles = names.Select(name => new DecoderRoleDraft(name, IsSpi && name != "clk", Channels,
            saved?.Inputs.FirstOrDefault(i => i.Role == name)?.Channel, canEdit)).ToArray();
        foreach (var role in Roles) role.Changed += OnChanged;
        baud = (saved?.Uart?.BaudRate ?? 115200).ToString(CultureInfo.InvariantCulture);
        bits = (saved?.Spi?.WordSize ?? saved?.Uart?.DataBits ?? 8).ToString(CultureInfo.InvariantCulture);
        order = saved?.Spi?.BitOrder ?? saved?.Uart?.BitOrder ?? (IsUart ? "lsb-first" : "msb-first");
        mode = saved?.Spi?.Mode ?? 0; polarity = saved?.Spi?.ChipSelectPolarity ?? "active-low";
        parity = saved?.Uart?.Parity ?? "none"; inverted = saved?.Uart?.Inverted ?? false;
        OnPropertyChanged(string.Empty);
    }
    private void OnChanged() => Changed?.Invoke();
    public DecoderRoleDraft Role(string role) => Roles.Single(r => r.Role == role);
    private string NextId()
    {
        if (existing != null) return existing.Id;
        var reserved = (capture?.EffectiveDecoderSources ?? []).Where(s => capture!.DecoderSources != null || capture.Packets.Any(p => p.Decoder == s.Id)).Select(s => s.Id).ToHashSet();
        string id = Code; int suffix = 1;
        while (reserved.Contains(id)) id = $"{Code}-{suffix++}";
        return id;
    }
    public bool TryCreate(out DecoderSource? result, out string problem)
    {
        result = null; problem = "";
        foreach (var role in Roles)
        {
            if (role.Selected == null || !role.Channels.Contains(role.Selected) || !role.Selected.Available)
            { problem = $"Choose a captured signal for {role.Label}."; return false; }
        }
        var inputs = Roles.Where(r => r.Selected!.Id >= 0).Select(r => new DecoderInput(r.Role, r.Selected!.Id)).ToArray();
        if (inputs.Select(i => i.Channel).Distinct().Count() != inputs.Length) { problem = "Each role needs a different signal."; return false; }
        if (IsSpi && !inputs.Any(i => i.Role is "mosi" or "miso")) { problem = "Choose MOSI, MISO, or both."; return false; }
        int bitCount = 8, baudRate = 115200;
        if (IsSerial && (!int.TryParse(Bits, NumberStyles.None, CultureInfo.InvariantCulture, out bitCount) || bitCount < (IsSpi ? 5 : 4) || bitCount > 128))
        { problem = IsSpi ? "Word size must be 5–128 bits." : "Data size must be 4–128 bits."; return false; }
        if (IsUart && (!int.TryParse(Baud, NumberStyles.None, CultureInfo.InvariantCulture, out baudRate) || baudRate <= 0))
        { problem = "Enter a positive baud rate."; return false; }
        if (IsUart && capture != null && capture.SampleRate < 2L * baudRate) { problem = "Capture rate must be at least twice the baud rate."; return false; }
        var binding = new DecoderSource(NextId(), inputs) { Protocol = Code,
            Spi = IsSpi ? new(Mode, Order, bitCount, Polarity) : null,
            Uart = IsUart ? new(baudRate, bitCount, Parity, 1, Order, Inverted) : null };
        try { DecoderSource.Validate((capture?.EffectiveDecoderSources ?? []).Where(s => s.Id != binding.Id).Append(binding).ToArray()); }
        catch (InvalidDataException ex) { problem = ex.Message; return false; }
        result = binding; return true;
    }
}
