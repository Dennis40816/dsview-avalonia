// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
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
    private readonly bool rulerOnly;
    private readonly List<(Rect Bounds, Packet Packet)> hits = [];
    private Point press;
    private double pressStart, pressEnd;
    private bool down, dragged;
    private int? dragEndpoint;
    private int? hoverChannel;
    private long hoverSample;
    private IReadOnlyList<Signal>? colorSignals;
    private readonly Dictionary<int, (Button Button, Border Swatch)> colorButtons = [];
    public Canvas LabelControls { get; } = new() { Width = LabelWidth, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
    public event Action<int>? ColorRequested;
    private static readonly string[] Probe = ["#505050", "#8F5202", "#CC0000", "#F57900", "#EDD400", "#73D216", "#3465A4", "#75507B"];
    public static IBrush Ink(string hex) => Brush.Parse(hex);
    private static readonly IBrush Text = Ink("#E4EAF0"), Muted = Ink("#9BAEBF"), Accent = Ink("#78D7C8"), Surface = Ink("#101820"), GridInk = Ink("#26343F");
    private static readonly Typeface Ui = new("fonts:Inter#Inter"), Mono = new("Cascadia Mono,Consolas");
    public TimelineControl(Workbench workbench, bool isOverview = false, bool isRuler = false)
    {
        model = workbench; overview = isOverview; rulerOnly = isRuler; ClipToBounds = true; Focusable = true; VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        if (overview) ToolTip.SetTip(this, "All decoded events across the capture. Undecoded signal activity is not included.");
        model.Changed += Refresh;
        Refresh();
        PointerPressed += Pressed; PointerMoved += Moved; PointerReleased += Released;
        PointerCaptureLost += (_, _) => { down = false; dragEndpoint = null; };
        PointerExited += (_, _) => { if (!down) { hoverChannel = null; InvalidateVisual(); } };
        PointerWheelChanged += (_, e) => { if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) { model.Zoom(e.Delta.Y > 0 ? .8 : 1.25, Math.Clamp((e.GetPosition(this).X - LabelWidth) / PlotWidth, 0, 1)); e.Handled = true; } };
    }
    private double PlotWidth => Math.Max(1, Bounds.Width - LabelWidth - 16);
    private double SignalsTop => model.Decoders.Count * 42 + 28;
    private double X(double sample) => LabelWidth + (sample - model.Start) / (model.End - model.Start) * PlotWidth;
    private long Sample(double x) => (long)Math.Round(model.Start + (x - LabelWidth) / PlotWidth * (model.End - model.Start));
    private int? ChannelAt(double y)
    {
        int index = (int)Math.Floor((y - SignalsTop) / RowHeight);
        return !rulerOnly && index >= 0 && index < model.Signals.Count ? model.Signals[index].Id : null;
    }
    private void Refresh()
    {
        if (!down) hoverChannel = null;
        Height = overview ? 40 : rulerOnly ? 42 : Math.Max(288, SignalsTop + model.Signals.Count * RowHeight + 16);
        if (!overview && !rulerOnly) RefreshColorButtons();
        InvalidateVisual();
    }
    private IBrush SignalColor(int channel) => Ink(model.Colors.GetValueOrDefault(channel, channel == 0 ? "#D4AD00" : "#C7D2DB"));
    private void RefreshColorButtons()
    {
        if (!ReferenceEquals(colorSignals, model.Signals))
        {
            colorSignals = model.Signals; LabelControls.Children.Clear(); colorButtons.Clear();
            foreach (var signal in colorSignals)
            {
                int channel = signal.Id;
                var swatch = new Border { Width = 12, Height = 4, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
                var content = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 3 };
                content.Children.Add(swatch); content.Children.Add(new TextBlock { Text = "▾", FontSize = 9, Foreground = Muted });
                var button = new Button { Content = content, Width = 30, Height = 28, MinHeight = 28, Padding = new Thickness(3), Background = Ink("#18242E"), BorderBrush = GridInk, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3) };
                AutomationProperties.SetName(button, $"D{channel} {signal.Name} waveform color");
                ToolTip.SetTip(button, $"Waveform color · D{channel} {signal.Name}");
                button.Click += (_, _) => ColorRequested?.Invoke(channel);
                Canvas.SetLeft(button, 134); LabelControls.Children.Add(button); colorButtons[channel] = (button, swatch);
            }
        }
        for (int index = 0; index < model.Signals.Count; index++)
        {
            var item = colorButtons[model.Signals[index].Id]; item.Swatch.Background = SignalColor(model.Signals[index].Id);
            Canvas.SetTop(item.Button, SignalsTop + index * RowHeight + 12);
        }
    }
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
        if (rulerOnly) Label(context, "Time · µs", 16, 13, Muted, 12);
        for (double tick = Math.Ceiling(model.Us(model.Start) / tickUs) * tickUs; tick <= model.Us(model.End); tick += tickUs)
        {
            var x = X(tick * model.Capture.SampleRate / 1_000_000);
            context.DrawLine(new Pen(GridInk, 1), new(x, rulerOnly ? 36 : 0), new(x, Height));
            var str = tick.ToString("0.###", CultureInfo.InvariantCulture);
            if (rulerOnly) Label(context, str, Math.Clamp(x - 10, LabelWidth, Bounds.Width - 55), 13, Muted, 12);
        }
        if (rulerOnly) { DrawMarker(context, model.A, "A", false); DrawMarker(context, model.B, "B", true); return; }
        foreach (var (decoder, index) in model.Decoders.Select((d, i) => (d, i)))
        {
            var y = index * 42;
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
                    var ft = Format(label, item.Kind == "ack" ? Muted : Ink("#C9F1EA"), item.Kind == "ack" ? 12 : 13, true);
                    if (ft.Width > width - 6 && detailed && item.Kind == "data" && label.StartsWith("0x", StringComparison.Ordinal))
                        ft = Format(label[2..], Ink("#C9F1EA"), 13, true);
                    if (ft.Width > width - 6 && !detailed && item.Kind == "data")
                        ft = Format(packet.Decoder == "i2c" ? packet.Address + " W" : $"{packet.Bytes.Length} B", Ink("#C9F1EA"), 13, true);
                    if (ft.Width <= width - 6) context.DrawText(ft, new(left + (width - ft.Width) / 2, y + 5 + (26 - ft.Height) / 2));
                    hits.Add((rect, packet));
                }
            }
        }
        Label(context, "Signals", 16, SignalsTop - 22, Muted, 12);
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
            using (context.PushClip(new Rect(57, y, 73, RowHeight))) Label(context, signal.Name, 57, y + 17, Text, 13);
            IBrush color = SignalColor(signal.Id);
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
        if (hoverChannel is int hc && model.Armed == null && !down && model.Signals.Any(s => s.Id == hc))
        {
            var measured = model.Capture.MeasureAt(hc, hoverSample);
            var y = SignalsTop + model.Signals.ToList().FindIndex(s => s.Id == hc) * RowHeight + 3;
            double labelX = Math.Clamp(X(hoverSample), LabelWidth, Math.Max(LabelWidth, Bounds.Width - 430));
            if (measured.Pulse is { } pulse)
            {
                double x1 = Math.Clamp(X(pulse.Start), LabelWidth, LabelWidth + PlotWidth), x2 = Math.Clamp(X(pulse.End), LabelWidth, LabelWidth + PlotWidth);
                context.DrawLine(new Pen(Accent, 1), new(x1, y), new(x2, y));
            }
            if (measured != model.PinnedMeasurement)
            {
                var preview = Format($"Preview D{hc} · " + TimingText.Local(measured, model.Capture.SampleRate), Ink("#C9F1EA"), 13);
                labelX = Math.Clamp(X(hoverSample), LabelWidth, Math.Max(LabelWidth, LabelWidth + PlotWidth - preview.Width - 8));
                // Keep the preview between the previous low line (rowTop - 17) and current high line (rowTop + 16).
                context.FillRectangle(Surface, new(labelX, y - 16, preview.Width + 8, 22));
                context.DrawText(preview, new(labelX + 4, y - 15));
            }
        }
    }
    private void DrawMarker(DrawingContext context, Endpoint? marker, string name, bool dashed)
    {
        if (marker == null || marker.Sample < model.Start || marker.Sample > model.End) return;
        var x = X(marker.Sample);
        context.DrawLine(new Pen(Accent, 1, dashed ? DashStyle.Dash : null), new(x, rulerOnly ? 35 : 0), new(x, Height));
        if (!rulerOnly) return;
        var other = name == "A" ? model.B : model.A;
        bool close = other != null && Math.Abs(X(other.Sample) - x) < 20;
        if (close && name == "B") return;
        double tagWidth = close ? 30 : 18, tagX = Math.Clamp(x - tagWidth / 2, LabelWidth, LabelWidth + PlotWidth - tagWidth);
        context.FillRectangle(Ink("#183D3A"), new(tagX, 28, tagWidth, 17));
        Label(context, close ? "A/B" : name, tagX + 4, 29, Accent, 11);
    }
    private void DrawOverview(DrawingContext context)
    {
        Label(context, "Decoded overview", 16, 13, Muted, 12);
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
        if (p.X < LabelWidth) return;
        if (p.X > LabelWidth + PlotWidth) return;
        press = p; pressStart = model.Start; pressEnd = model.End; down = true; dragged = false;
        double distanceA = model.A is { } a ? Math.Abs(X(a.Sample) - p.X) : double.PositiveInfinity;
        double distanceB = model.B is { } b ? Math.Abs(X(b.Sample) - p.X) : double.PositiveInfinity;
        dragEndpoint = Math.Min(distanceA, distanceB) > 7 ? null : Math.Abs(distanceA - distanceB) < .01 ? model.Armed ?? 0 : distanceA < distanceB ? 0 : 1;
        e.Pointer.Capture(this); e.Handled = true;
    }
    private void Moved(object? sender, PointerEventArgs e)
    {
        var p = e.GetPosition(this); hoverChannel = p.X >= LabelWidth && p.X <= LabelWidth + PlotWidth ? ChannelAt(p.Y) : null; hoverSample = Sample(p.X);
        if (!overview && !rulerOnly)
        {
            var labelChannel = p.X < LabelWidth ? ChannelAt(p.Y) : null;
            ToolTip.SetTip(this, labelChannel is int id && p.X < 134 ? $"D{id} · {model.Signals.First(s => s.Id == id).Name} · Physical probe identity" : null);
        }
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
