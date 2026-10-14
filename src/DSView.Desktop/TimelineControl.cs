// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using DSView.Domain;

namespace DSView.Desktop;

public sealed class TimelineControl : Control
{
    public const double LabelWidth = 168, RowHeight = 52;
    private readonly Workbench model;
    private readonly bool overview;
    private readonly List<(Rect Bounds, Packet Packet)> hits = [];
    private Point press;
    private double pressStart, pressEnd;
    private bool down, dragged;
    private int? dragEndpoint;
    private int? hoverChannel;
    private long hoverSample;
    public event Action<int>? ColorRequested;
    private static readonly string[] Probe = ["#505050", "#8F5202", "#CC0000", "#F57900", "#EDD400", "#73D216", "#3465A4", "#75507B"];
    public static IBrush Ink(string hex) => Brush.Parse(hex);
    private static readonly IBrush Text = Ink("#E4EAF0"), Muted = Ink("#9BAEBF"), Accent = Ink("#78D7C8"), Surface = Ink("#101820"), GridInk = Ink("#26343F");
    private static readonly Typeface Ui = new("fonts:Inter#Inter"), Mono = new("Cascadia Mono,Consolas");
    public TimelineControl(Workbench workbench, bool isOverview = false)
    {
        model = workbench; overview = isOverview; ClipToBounds = true; Focusable = true; VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        model.Changed += Refresh;
        Refresh();
        PointerPressed += Pressed; PointerMoved += Moved; PointerReleased += Released;
        PointerCaptureLost += (_, _) => { down = false; dragEndpoint = null; };
        PointerExited += (_, _) => { if (!down) { hoverChannel = null; InvalidateVisual(); } };
        PointerWheelChanged += (_, e) => { if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) { model.Zoom(e.Delta.Y > 0 ? .8 : 1.25, Math.Clamp((e.GetPosition(this).X - LabelWidth) / PlotWidth, 0, 1)); e.Handled = true; } };
    }
    private double PlotWidth => Math.Max(1, Bounds.Width - LabelWidth - 16);
    private double SignalsTop => 42 + model.Decoders.Count * 42 + 28;
    private double X(double sample) => LabelWidth + (sample - model.Start) / (model.End - model.Start) * PlotWidth;
    private long Sample(double x) => (long)Math.Round(model.Start + (x - LabelWidth) / PlotWidth * (model.End - model.Start));
    private int? ChannelAt(double y)
    {
        int index = (int)Math.Floor((y - SignalsTop) / RowHeight);
        return index >= 0 && index < model.Signals.Count ? model.Signals[index].Id : null;
    }
    private void Refresh() { Height = overview ? 48 : Math.Max(330, SignalsTop + model.Signals.Count * RowHeight + 16); InvalidateVisual(); }
    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Surface, Bounds.WithX(0).WithY(0));
        if (model.Capture == null) { Label(context, "Loading saved demo…", 18, 16, Muted); return; }
        if (overview) { DrawOverview(context); return; }
        hits.Clear();
        var spanUs = model.Us(model.End - model.Start);
        var rough = spanUs / Math.Max(2, PlotWidth / 110);
        var power = Math.Pow(10, Math.Floor(Math.Log10(rough)));
        var tickUs = new[] { 1d, 2, 5, 10 }.First(n => n * power >= rough) * power;
        Label(context, "Time · µs", 16, 13, Muted, 12);
        for (double tick = Math.Ceiling(model.Us(model.Start) / tickUs) * tickUs; tick <= model.Us(model.End); tick += tickUs)
        {
            var x = X(tick * model.Capture.SampleRate / 1_000_000);
            context.DrawLine(new Pen(GridInk, 1), new(x, 36), new(x, Height));
            var str = tick.ToString("0.###", CultureInfo.InvariantCulture);
            Label(context, str, Math.Clamp(x - 10, LabelWidth, Bounds.Width - 55), 13, Muted, 12);
        }
        foreach (var (decoder, index) in model.Decoders.Select((d, i) => (d, i)))
        {
            var y = 42 + index * 42;
            Label(context, decoder.Name, 16, y + 9, Text, 13);
            using (context.PushClip(new Rect(LabelWidth, y, PlotWidth, 38)))
            foreach (var packet in model.Capture.Packets.Where(p => p.Decoder == decoder.Id && p.Span.End >= model.Start && p.Span.Start <= model.End))
            {
                bool detailed = decoder.Id == "i2c" && 1000d / (model.End - model.Start) * PlotWidth >= 43;
                var items = detailed ? model.Capture.ProtocolItems(packet) : [new ProtocolItem(packet, packet.Decoder == "i2c" ? packet.Address + " W · " + packet.Hex : packet.Hex, packet.Span, "data")];
                foreach (var item in items)
                {
                    if (item.Span.End < model.Start || item.Span.Start > model.End) continue;
                    var left = Math.Max(LabelWidth, X(item.Span.Start)); var right = Math.Min(LabelWidth + PlotWidth, X(item.Span.End));
                    var width = Math.Max(1, right - left); var rect = new Rect(left, y + 5, width, 26);
                    if (item.Kind == "point")
                    {
                        context.DrawLine(new Pen(Muted, 1), new(left, y + 3), new(left, y + 32));
                        Label(context, item.Label == "START" ? "S" : "P", Math.Clamp(left + 2, LabelWidth, LabelWidth + PlotWidth - 11), y + 10, Muted, 11);
                        hits.Add((new(left - 5, y, 14, 36), packet)); continue;
                    }
                    IBrush fill = item.Kind == "ack" ? Ink("#25303A") : Ink(model.Selected == packet ? "#245A53" : "#254347");
                    if (item.Kind == "ack" || width < 10) context.FillRectangle(fill, rect);
                    else
                    {
                        var geometry = new StreamGeometry();
                        using (var g = geometry.Open())
                        {
                            var cap = Math.Min(7, width / 4);
                            var lc = item.Span.Start < model.Start ? 0 : cap; var rc = item.Span.End > model.End ? 0 : cap;
                            g.BeginFigure(new(left + lc, y + 5), true); g.LineTo(new(right - rc, y + 5)); g.LineTo(new(right, y + 18));
                            g.LineTo(new(right - rc, y + 31)); g.LineTo(new(left + lc, y + 31)); g.LineTo(new(left, y + 18)); g.EndFigure(true);
                        }
                        context.DrawGeometry(fill, null, geometry);
                    }
                    string label = item.Label;
                    if (item.Kind == "ack" && width < 32) label = "A";
                    var ft = Format(label, item.Kind == "ack" ? Muted : Accent, 11.5, true);
                    if (ft.Width <= width - 6) context.DrawText(ft, new(left + (width - ft.Width) / 2, y + 10));
                    hits.Add((rect, packet));
                }
            }
        }
        Label(context, $"Signals  {model.Signals.Count} of 32", 16, SignalsTop - 22, Muted, 12);
        context.DrawLine(new Pen(GridInk, 1), new(0, SignalsTop - 7), new(Bounds.Width, SignalsTop - 7));
        if (model.Selected is { } selected && model.SelectionInGroup)
        {
            var left = Math.Max(LabelWidth, X(selected.Span.Start)); var right = Math.Min(LabelWidth + PlotWidth, X(selected.Span.End));
            if (right > left) context.FillRectangle(Ink("#132B2C"), new(left, SignalsTop, right - left, model.Signals.Count * RowHeight));
        }
        foreach (var (signal, index) in model.Signals.Select((s, i) => (s, i)))
        {
            var y = SignalsTop + index * RowHeight;
            context.FillRectangle(Ink(Probe[signal.Id % 8]), new(15, y + 16, 5, 20));
            Label(context, $"D{signal.Id}", 26, y + 18, Muted, 12);
            Label(context, signal.Name, 57, y + 17, Text, 12);
            IBrush color = Ink(model.Colors.GetValueOrDefault(signal.Id, signal.Id == 0 ? "#D4AD00" : "#C7D2DB"));
            context.DrawLine(new Pen(color, 2), new(142, y + 26), new(158, y + 26));
            using (context.PushClip(new Rect(LabelWidth, y, PlotWidth, RowHeight)))
            {
                var edgeIndex = signal.LowerBound((long)Math.Ceiling(model.Start));
                bool high = edgeIndex > 0 ? signal.Edges[edgeIndex - 1].High : signal.Initial;
                double left = LabelWidth, level = y + (high ? 16 : 35);
                var geometry = new StreamGeometry();
                using (var g = geometry.Open())
                {
                    g.BeginFigure(new(left, level), false);
                    for (int i = edgeIndex; i < signal.Edges.Count && signal.Edges[i].Sample <= model.End; i++)
                    {
                        var edge = signal.Edges[i]; var x = X(edge.Sample);
                        g.LineTo(new(x, level)); level = y + (edge.High ? 16 : 35); g.LineTo(new(x, level));
                    }
                    g.LineTo(new(LabelWidth + PlotWidth, level)); g.EndFigure(false);
                }
                context.DrawGeometry(null, new Pen(color, 1.35), geometry);
            }
        }
        DrawMarker(context, model.A, "A", false); DrawMarker(context, model.B, "B", true);
        if (hoverChannel is int hc && model.Mode == 0 && !down)
        {
            var pair = model.Capture.Signals.First(s => s.Id == hc).Surrounding(hoverSample);
            if (pair is { } p)
            {
                var y = SignalsTop + model.Signals.ToList().FindIndex(s => s.Id == hc) * RowHeight + 3;
                double x1 = Math.Clamp(X(p.A.Sample), LabelWidth, LabelWidth + PlotWidth), x2 = Math.Clamp(X(p.B.Sample), LabelWidth, LabelWidth + PlotWidth);
                context.DrawLine(new Pen(Accent, 1), new(x1, y), new(x2, y));
                Label(context, $"Δ {model.Us(p.B.Sample - p.A.Sample):0.###} µs", Math.Clamp(x1 + 5, LabelWidth, Bounds.Width - 110), y - 13, Accent, 11);
            }
        }
    }
    private void DrawMarker(DrawingContext context, Endpoint? marker, string name, bool dashed)
    {
        if (marker == null || marker.Sample < model.Start || marker.Sample > model.End) return;
        var x = X(marker.Sample);
        context.DrawLine(new Pen(Accent, 1, dashed ? DashStyle.Dash : null), new(x, 35), new(x, Height));
        context.FillRectangle(Ink("#183D3A"), new(Math.Clamp(x - 9, LabelWidth, LabelWidth + PlotWidth - 18), 28, 18, 17));
        Label(context, name, Math.Clamp(x - 4, LabelWidth + 5, LabelWidth + PlotWidth - 13), 29, Accent, 11);
    }
    private void DrawOverview(DrawingContext context)
    {
        Label(context, "Overview · 1 ms", 16, 16, Muted, 12);
        context.FillRectangle(Ink("#18242E"), new(LabelWidth, 7, PlotWidth, 33));
        foreach (var packet in model.Capture!.Packets)
        {
            double x = LabelWidth + packet.Span.Start / (double)model.Capture.SampleCount * PlotWidth;
            var width = Math.Max(2, (packet.Span.End - packet.Span.Start) / (double)model.Capture.SampleCount * PlotWidth);
            var lane = Array.FindIndex(DSView.Domain.Capture.Decoders, d => d.Id == packet.Decoder);
            context.FillRectangle(packet.Error == null ? Ink("#506270") : Ink("#DE8794"), new(x, 11 + lane * 6, width, 3));
        }
        var a = LabelWidth + model.Start / model.Capture.SampleCount * PlotWidth;
        var b = LabelWidth + model.End / model.Capture.SampleCount * PlotWidth;
        context.DrawRectangle(Ink("#284F655E"), new Pen(Accent, 1), new(a, 7, Math.Max(2, b - a), 33));
    }
    private static FormattedText Format(string value, IBrush brush, double size, bool mono = false) => new(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, mono ? Mono : Ui, size, brush);
    private static void Label(DrawingContext c, string value, double x, double y, IBrush brush, double size = 13) => c.DrawText(Format(value, brush, size), new(x, y));
    private void Pressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || model.Capture == null) return;
        Focus(); var p = e.GetPosition(this);
        if (overview) { model.Remember(); var center = Math.Clamp((p.X - LabelWidth) / PlotWidth, 0, 1) * model.Capture.SampleCount; var half = (model.End - model.Start) / 2; model.SetRange(center - half, center + half); e.Handled = true; return; }
        if (p.X < LabelWidth) { if (p.X >= 135 && ChannelAt(p.Y) is int c) ColorRequested?.Invoke(c); return; }
        press = p; pressStart = model.Start; pressEnd = model.End; down = true; dragged = false;
        double distanceA = model.A is { } a ? Math.Abs(X(a.Sample) - p.X) : double.PositiveInfinity;
        double distanceB = model.B is { } b ? Math.Abs(X(b.Sample) - p.X) : double.PositiveInfinity;
        dragEndpoint = Math.Min(distanceA, distanceB) > 7 ? null : Math.Abs(distanceA - distanceB) < .01 ? model.Armed ?? 0 : distanceA < distanceB ? 0 : 1;
        e.Pointer.Capture(this); e.Handled = true;
    }
    private void Moved(object? sender, PointerEventArgs e)
    {
        var p = e.GetPosition(this); hoverChannel = p.X >= LabelWidth ? ChannelAt(p.Y) : null; hoverSample = Sample(p.X);
        if (!down) { InvalidateVisual(); return; }
        if (!dragged && Math.Abs(p.X - press.X) > 4) { dragged = true; if (dragEndpoint == null) model.Remember(); }
        if (!dragged) return;
        if (dragEndpoint is int target)
        {
            var previous = target == 0 ? model.A : model.B; var channel = ChannelAt(p.Y) ?? previous?.Channel;
            if (channel is int id) model.Pick(id, Sample(p.X), (model.End - model.Start) / PlotWidth * 8, target);
        }
        else { var delta = (p.X - press.X) / PlotWidth * (pressEnd - pressStart); model.SetRange(pressStart - delta, pressEnd - delta); }
    }
    private void Released(object? sender, PointerReleasedEventArgs e)
    {
        if (!down) return;
        down = false; var p = e.GetPosition(this); e.Pointer.Capture(null);
        if (!dragged)
        {
            var hit = hits.FirstOrDefault(h => h.Bounds.Contains(p));
            if (hit.Packet != null) model.Select(hit.Packet);
            else if (ChannelAt(p.Y) is int channel) model.Pick(channel, Sample(p.X), (model.End - model.Start) / PlotWidth * 8);
        }
        dragEndpoint = null; e.Handled = true;
    }
}
