// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DSView.Domain;

namespace DSView.Desktop;

public sealed class Workbench : ObservableObject
{
    public Capture? Capture { get; private set; }
    public ObservableCollection<ObservationGroup> Groups { get; } = [new("I²C + Control", [0, 1, 8], ["i2c"]), new("UART + SPI", [2, 3, 4, 5, 6, 7], ["spi", "ua", "ub"]), new("All signals", Enumerable.Range(0, 32).ToArray(), [])];
    public ObservationGroup Group { get; private set; }
    public Dictionary<int, string> Colors { get; } = [];
    public double Start { get; private set; } = 24_000;
    public double End { get; private set; } = 33_500;
    public Endpoint? A { get; private set; }
    public Endpoint? B { get; private set; }
    public int Mode { get; private set; }
    public int? Armed { get; private set; }
    public Packet? Selected { get; private set; }
    public bool Details { get; private set; }
    public bool PacketsTab { get; private set; }
    public string Hint { get; private set; } = "Click a signal for its two surrounding edges. Ctrl + wheel to zoom; drag to pan.";
    public string Query { get; private set; } = "";
    private readonly Stack<(double Start, double End, ObservationGroup Group)> history = new();
    public event Action? Changed;
    public Workbench() => Group = Groups[0];
    public IReadOnlyList<Signal> Signals => Capture?.Signals.Where(s => Group.Channels.Contains(s.Id)).ToArray() ?? [];
    public IReadOnlyList<Decoder> Decoders => DSView.Domain.Capture.Decoders.Where(d => Group.Decoders.Contains(d.Id)).ToArray();
    public bool IsAllSignals => Group.Name == "All signals";
    public IEnumerable<Packet> Packets => (Capture?.Packets ?? []).Where(p => (IsAllSignals || Group.Decoders.Contains(p.Decoder)) && (string.IsNullOrWhiteSpace(Query) || $"{p.Id} {p.Hex} {p.Address} {p.Title} {p.Error} 0x{p.Value:X}".Contains(Query, StringComparison.OrdinalIgnoreCase)));
    public bool SelectionInGroup => Selected == null || IsAllSignals || Group.Decoders.Contains(Selected.Decoder);
    public double Us(double sample) => sample / (Capture?.SampleRate ?? 50_000_000) * 1_000_000;
    public string RangeText => $"{Us(Start):0.##}–{Us(End):0.##} µs";
    public void Load(Capture capture) { Capture = capture; Notify(); }
    public void Notify() { OnPropertyChanged(string.Empty); Changed?.Invoke(); }
    public void SetGroup(ObservationGroup group) { Group = group; if (!SelectionInGroup) Details = false; Notify(); }
    public void ShowAll() { PacketsTab = false; SetGroup(Groups.First(g => g.Name == "All signals")); }
    public void Select(Packet packet) { Selected = packet; Details = true; Notify(); }
    public void ToggleDetails() { Details = !Details; Notify(); }
    public void SetTab(bool packets) { PacketsTab = packets; Notify(); }
    public void Search(string value) { Query = value; Notify(); }
    public void Remember() { history.Push((Start, End, Group)); if (history.Count > 100) { var keep = history.Take(50).Reverse().ToArray(); history.Clear(); foreach (var item in keep) history.Push(item); } }
    public void Previous() { if (history.TryPop(out var prior)) { Group = Groups.Contains(prior.Group) ? prior.Group : Group; SetRange(prior.Start, prior.End); } }
    public void SetRange(double start, double end)
    {
        double total = Capture?.SampleCount ?? 50_000, span = Math.Clamp(end - start, 10, total);
        Start = Math.Clamp(start, 0, total - span); End = Start + span; Notify();
    }
    public void Zoom(double factor, double anchor = .5) { Remember(); var sample = Start + anchor * (End - Start); var span = (End - Start) * factor; SetRange(sample - anchor * span, sample + (1 - anchor) * span); }
    public void FitTime() { Remember(); SetRange(0, Capture?.SampleCount ?? 50_000); }
    public void Locate()
    {
        if (Selected == null) return;
        Remember(); Group = Groups.FirstOrDefault(g => g.Decoders.Contains(Selected.Decoder)) ?? Group;
        PacketsTab = false; var span = Selected.Span.End - Selected.Span.Start; SetRange(Selected.Span.Start - span * .08, Selected.Span.End + span * .08);
    }
    public void SetMode(int mode) { Mode = mode; Armed = mode == 0 ? null : 0; Hint = mode == 0 ? "Click a signal for its two surrounding edges." : "Choose A on any signal, then choose B. Drag a cursor to adjust."; Notify(); }
    public void Arm(int endpoint) { if (Mode == 0) Mode = 1; Armed = endpoint; Hint = $"Choose {(endpoint == 0 ? "A" : "B")} on any signal."; Notify(); }
    public void Clear() { A = B = null; Armed = Mode == 0 ? null : 0; Notify(); }
    public void Pick(int channel, long sample, double tolerance, int? replacing = null)
    {
        var signal = Capture?.Signals.First(s => s.Id == channel);
        if (signal == null) return;
        if (Mode == 0 && replacing == null)
        {
            var pair = signal.Surrounding(sample);
            if (pair == null) { Hint = $"D{channel} has fewer than two edges. Choose A/B position to measure here."; Notify(); return; }
            A = new(channel, pair.Value.A.Sample, true); B = new(channel, pair.Value.B.Sample, true);
            Hint = $"D{channel} · adjacent edges (B − A)."; Notify(); return;
        }
        int? target = replacing ?? Armed;
        if (target == null) { Hint = "Choose A or B to replace an endpoint, or drag an existing cursor."; Notify(); return; }
        var edge = signal.Nearest(sample);
        if (Mode != 2 && (edge == null || Math.Abs(edge.Sample - sample) > tolerance || edge.Sample < Start || edge.Sample > End))
        { Hint = "No nearby edge. Zoom in or use A/B position."; Notify(); return; }
        if (replacing != null && Mode == 0) Mode = 1;
        var point = new Endpoint(channel, Mode == 2 ? Math.Clamp(sample, 0, Capture!.SampleCount - 1) : edge!.Sample, Mode != 2);
        if (target == 0) A = point; else B = point;
        Armed = replacing != null ? null : target == 0 ? 1 : null;
        Hint = Armed == 1 ? "A set. Choose B on any signal." : "Drag either cursor to refine; Δt = B − A.";
        Notify();
    }
    public bool EditEndpoint(int index, double microseconds)
    {
        var old = index == 0 ? A : B;
        if (old == null || Capture == null || !double.IsFinite(microseconds) || microseconds < 0 || microseconds > Us(Capture.SampleCount - 1)) return false;
        var sample = Capture.ToSample(microseconds);
        if (sample == old.Sample) return true;
        var point = old with { Sample = sample, IsEdge = false };
        if (index == 0) A = point; else B = point;
        Mode = 2; Hint = "Position quantized to the 20 ns capture sample grid."; Notify(); return true;
    }
    public void FitMarkers()
    {
        if (A == null || B == null) return;
        Remember(); Group = Groups.FirstOrDefault(g => g.Channels.Contains(A.Channel) && g.Channels.Contains(B.Channel)) ?? Groups[^1];
        var first = Math.Min(A.Sample, B.Sample); var last = Math.Max(A.Sample, B.Sample); var pad = Math.Max(125, (last - first) * .15);
        PacketsTab = false; SetRange(first - pad, last + pad);
    }
    public void StepEdge(int channel, int direction)
    {
        var signal = Capture?.Signals.First(s => s.Id == channel); if (signal == null) return;
        var center = (Start + End) / 2;
        Edge? edge = direction > 0 ? signal.Edges.FirstOrDefault(e => e.Sample > center + 1) : signal.Edges.LastOrDefault(e => e.Sample < center - 1);
        if (edge == null) { Hint = "No further edge in this capture."; Notify(); return; }
        Remember(); var span = End - Start; SetRange(edge.Sample - span / 2, edge.Sample + span / 2);
    }
}
