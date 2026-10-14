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
    private int? armedOnPress;
    private MarkerHandle? pressedHandle;
    private Capture? pressCapture;
    private long dragSampleOrigin;
    private IPointer? activePointer;
    private bool keyboardMode;
    private int? hoverChannel;
    private long hoverSample;
    private IReadOnlyList<Signal>? colorSignals;
    private readonly Dictionary<int, (Button Button, Border Swatch, TextBlock Activity)> colorButtons = [];
    private Capture? overviewCapture;
    private IReadOnlyList<Signal>? overviewSignals;
    private bool[] activityBins = [];
    private (Capture? Capture, double Start, double End, double Width) traceKey;
    private readonly Dictionary<Signal, (StreamGeometry Geometry, Rect[] Dense)> traces = new(ReferenceEqualityComparer.Instance);
    private double visibleTop, visibleBottom = double.PositiveInfinity;
    private readonly List<(Rect Bounds, CaptureIssue Issue)> issueHits = [];
    public Canvas LabelControls { get; } = new() { Width = LabelWidth, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
    public event Action<int>? ColorRequested;
    public event Action<int>? EndpointRequested;
    public event Action? EditingCancelled;
    public event Action<bool>? KeyboardModeChanged;
    private static readonly string[] Probe = ["#505050", "#8F5202", "#CC0000", "#F57900", "#EDD400", "#73D216", "#3465A4", "#75507B"];
    public static IBrush Ink(string hex) => Brush.Parse(hex);
    private static readonly IBrush Text = Ink("#E4EAF0"), Muted = Ink("#9BAEBF"), Accent = Ink("#78D7C8"), Surface = Ink("#101820"), GridInk = Ink("#26343F");
    private static readonly Typeface Ui = new("fonts:Inter#Inter"), Mono = new("Cascadia Mono,Consolas");
    public TimelineControl(Workbench workbench, bool isOverview = false, bool isRuler = false)
    {
        model = workbench; overview = isOverview; rulerOnly = isRuler; ClipToBounds = true; Focusable = true; VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        if (overview) ToolTip.SetTip(this, "Observed raw transitions in this Group across the full capture. Flags mark this Group's diagnostic evidence. Click the map to move the time view; use Capture issues to locate a diagnostic.");
        model.Changed += Refresh;
        Refresh();
        PointerPressed += Pressed; PointerMoved += Moved; PointerReleased += Released;
        PointerCaptureLost += (_, _) => { if (down && dragged && dragEndpoint != null) model.CancelPick(); down = false; dragEndpoint = null; activePointer = null; };
        KeyDown += Keyboard;
        GotFocus += (_, e) => { if (!overview && !rulerOnly && e.NavigationMethod == NavigationMethod.Tab) { model.FocusProbe(); SetKeyboardMode(true); } };
        LostFocus += (_, _) => SetKeyboardMode(false);
        PointerExited += (_, _) => { if (!down) { hoverChannel = null; InvalidateVisual(); } };
        PointerWheelChanged += (_, e) => { if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) { model.Zoom(e.Delta.Y > 0 ? .8 : 1.25, Math.Clamp((e.GetPosition(this).X - LabelWidth) / PlotWidth, 0, 1)); e.Handled = true; } };
    }
    private double PlotWidth => Math.Max(1, Bounds.Width - LabelWidth - 16);
    private IReadOnlyList<MarkerHandle> Handles => MarkerHandles.Layout(model.A is { } a ? X(a.Sample) : null, model.B is { } b ? X(b.Sample) : null, LabelWidth, LabelWidth + PlotWidth);
    private void SetKeyboardMode(bool enabled) { keyboardMode = enabled; KeyboardModeChanged?.Invoke(enabled); Refresh(); }
    public void FocusForPick() { Focus(); model.FocusProbe(); SetKeyboardMode(true); }
    private void Keyboard(object? sender, KeyEventArgs e)
    {
        if (overview || !IsFocused || e.KeyModifiers != KeyModifiers.None) return;
        if (e.Key == Key.Escape)
        {
            if (!down && model.Armed == null && !keyboardMode) return;
            down = false; dragEndpoint = null; activePointer?.Capture(null); activePointer = null;
            if (EditingCancelled != null) EditingCancelled(); else model.CancelPick();
            SetKeyboardMode(false); e.Handled = true; return;
        }
        if (rulerOnly || e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down or Key.Enter)) return;
        SetKeyboardMode(true);
        if (e.Key == Key.Enter) model.CommitProbe();
        else model.MoveProbe(e.Key == Key.Up ? -1 : e.Key == Key.Down ? 1 : 0, e.Key == Key.Left ? -1 : e.Key == Key.Right ? 1 : 0);
        e.Handled = true;
    }
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
        if (!overview && !rulerOnly)
            AutomationProperties.SetName(this, model.Probe is { } probe && keyboardMode
                ? $"Waveforms · D{probe.Channel} · {model.Us(probe.Sample):0.###} µs · {(probe.IsEdge ? "edge" : "position")}. Arrow keys move; Enter selects; Escape cancels."
                : "Waveforms. Arrow keys select a signal and edge; Enter measures.");
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
                var activity = new TextBlock { FontSize = 11, Foreground = Muted, Width = 104, TextTrimming = TextTrimming.CharacterEllipsis };
                Canvas.SetLeft(activity, 26); LabelControls.Children.Add(activity);
                Canvas.SetLeft(button, 134); LabelControls.Children.Add(button); colorButtons[channel] = (button, swatch, activity);
            }
        }
        for (int index = 0; index < model.Signals.Count; index++)
        {
            var item = colorButtons[model.Signals[index].Id]; item.Swatch.Background = SignalColor(model.Signals[index].Id);
            Canvas.SetTop(item.Button, SignalsTop + index * RowHeight + 12);
            var activity = model.Capture!.Activity(model.Signals[index].Id, (long)Math.Ceiling(model.Start), (long)Math.Ceiling(model.End));
            item.Activity.Text = ActivityText.Short(activity);
            ToolTip.SetTip(item.Activity, "Current time view · " + ActivityText.Full(activity));
            AutomationProperties.SetName(item.Activity, $"D{model.Signals[index].Id} current view · {ActivityText.Full(activity)}");
            Canvas.SetTop(item.Activity, SignalsTop + index * RowHeight + 29);
        }
    }
    private SignalRow? SourceRow(int channel)
    {
        int index = model.Signals.ToList().FindIndex(s => s.Id == channel);
        return index < 0 ? null : new(SignalsTop + index * RowHeight, SignalsTop + (index + 1) * RowHeight);
    }
    public RowVisibility SourceVisibility(int channel) => SourceRow(channel) is { } row
        ? SignalViewport.Visibility(row, visibleTop, visibleBottom - visibleTop) : RowVisibility.Visible;
    public double RevealOffset(IReadOnlyList<int> channels, double offset, double height, double extent) =>
        SignalViewport.RevealOffset(channels.Select(SourceRow).OfType<SignalRow>().ToArray(), offset, height, extent);
    public void SetViewport(double top, double height)
    {
        if (height <= 0) return;
        visibleTop = top; visibleBottom = top + height; InvalidateVisual();
    }
    private (StreamGeometry Geometry, Rect[] Dense) Trace(Signal signal)
    {
        var key = (model.Capture, model.Start, model.End, PlotWidth);
        if (!ReferenceEquals(traceKey.Capture, model.Capture) || traceKey.Start != model.Start || traceKey.End != model.End || traceKey.Width != PlotWidth) { traces.Clear(); traceKey = key; }
        if (traces.TryGetValue(signal, out var cached)) return cached;
        var projection = WaveformProjection.Create(signal, model.Start, model.End, Math.Max(1, (int)Math.Ceiling(PlotWidth)));
        var geometry = new StreamGeometry(); var dense = new List<Rect>();
        double level = projection.Initial ? 16 : 35;
        using (var g = geometry.Open())
        {
            g.BeginFigure(new(LabelWidth, level), false);
            foreach (var run in projection.Runs)
            {
                double first = X(run.First.Sample), last = X(run.Last.Sample);
                g.LineTo(new(first, level));
                level = run.Last.High ? 16 : 35;
                if (run.Count == 1) g.LineTo(new(first, level));
                else
                {
                    // A dense band asserts activity, not a representative clock waveform.
                    g.EndFigure(false); dense.Add(new(first, 16, Math.Max(.5, last - first), 19));
                    g.BeginFigure(new(last, level), false);
                }
            }
            g.LineTo(new(LabelWidth + PlotWidth, level)); g.EndFigure(false);
        }
        return traces[signal] = (geometry, dense.ToArray());
    }
    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Surface, Bounds.WithX(0).WithY(0));
        if (model.Capture == null) { Label(context, "Loading saved demo…", 18, 16, Muted); return; }
        if (overview) { DrawOverview(context); return; }
        hits.Clear(); issueHits.Clear();
        var axis = TimeAxis.Create(model.Start, model.End, model.Capture.SampleRate, PlotWidth);
        if (rulerOnly)
        {
            Label(context, (axis.OriginText == null ? "Time · " : "Offset · ") + axis.Unit, 16, axis.OriginText == null ? 13 : 3, Muted, 12);
            if (axis.OriginText != null) Label(context, "from " + axis.OriginText, 16, 21, Muted, 10);
        }
        double labelRight = LabelWidth - 12;
        foreach (var tick in axis.Ticks)
        {
            var x = X(tick.Sample);
            context.DrawLine(new Pen(GridInk, 1), new(x, rulerOnly ? 36 : 0), new(x, Height));
            if (!rulerOnly) continue;
            var text = Format(tick.Label, Muted, 12);
            if (text.Width > PlotWidth) continue;
            double left = Math.Clamp(x - text.Width / 2, LabelWidth, LabelWidth + PlotWidth - text.Width);
            if (left < labelRight + 12) continue;
            context.DrawText(text, new(left, 13)); labelRight = left + text.Width;
        }
        if (rulerOnly) { DrawMarker(context, model.A, "A", false); DrawMarker(context, model.B, "B", true); return; }
        foreach (var (decoder, index) in model.Decoders.Select((d, i) => (d, i)))
        {
            var y = index * 42;
            Label(context, decoder.Name, 16, y + 9, Text, 13);
            using (context.PushClip(new Rect(LabelWidth, y, PlotWidth, 38)))
            foreach (var packet in model.Capture.Packets.Where(p => p.Decoder == decoder.Id && p.Span.End >= model.Start && p.Span.Start <= model.End))
            {
                var protocol = model.Capture.ProtocolItems(packet);
                bool pointsOnly = packet.Annotations != null && protocol.All(item => item.Kind == "point");
                bool detailed = pointsOnly || (packet.Annotations != null || packet.WireCells.Count > 0) && protocol.Any(item => item.Kind == "data" && (item.Span.End - item.Span.Start) / (model.End - model.Start) * PlotWidth >= 43);
                var items = detailed ? protocol : [new ProtocolItem(packet, packet.Decoder == "i2c" ? packet.Address + " " + packet.Direction + " · " + packet.Hex : packet.Hex, packet.Span, "data")];
                foreach (var item in items)
                {
                    if (item.Span.End < model.Start || item.Span.Start > model.End) continue;
                    var left = Math.Max(LabelWidth, X(item.Span.Start)); var right = Math.Min(LabelWidth + PlotWidth, X(item.Span.End));
                    var width = Math.Max(1, right - left); var rect = new Rect(left, y + 5, width, 26);
                    if (item.Kind == "point")
                    {
                        context.DrawLine(new Pen(Muted, 1), new(left, y + 3), new(left, y + 32));
                        string pointLabel = item.Label switch { "START" => "S", "RESTART" or "START REPEAT" => "Sr", "STOP" => "P", _ => item.Label };
                        Label(context, pointLabel, Math.Clamp(left + 2, LabelWidth, LabelWidth + PlotWidth - 18), y + 10, Muted, 11);
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
                    if (item.Kind == "ack" && width < 32) label = item.Label == "ACK" ? "A" : item.Label == "NACK" ? "N" : "?";
                    var ft = Format(label, item.Kind == "ack" ? Muted : Ink("#C9F1EA"), item.Kind == "ack" ? 12 : 13, true);
                    if (ft.Width > width - 6 && detailed && item.Kind == "data" && label.StartsWith("0x", StringComparison.Ordinal))
                        ft = Format(label[2..], Ink("#C9F1EA"), 13, true);
                    if (ft.Width > width - 6 && !detailed && item.Kind == "data")
                        ft = Format(packet.Decoder == "i2c" ? packet.Address + " " + packet.Direction : $"{packet.Bytes.Length} B", Ink("#C9F1EA"), 13, true);
                    if (ft.Width <= width - 6) context.DrawText(ft, new(left + (width - ft.Width) / 2, y + 5 + (26 - ft.Height) / 2));
                    hits.Add((rect, packet));
                }
                foreach (var issue in model.Issues.Where(i => ReferenceEquals(i.Packet, packet)))
                foreach (var source in issue.Diagnostic.Spans.DefaultIfEmpty(packet.Span))
                {
                    if (source.End < model.Start || source.Start > model.End) continue;
                    double left = Math.Max(LabelWidth, X(source.Start)), right = Math.Min(LabelWidth + PlotWidth, X(source.End));
                    var mark = new Rect(left, y + 33, Math.Max(2, right - left), 3);
                    context.FillRectangle(Ink("#FF9AAA"), mark);
                    Label(context, "!", Math.Min(left, LabelWidth + PlotWidth - 9), y + 30, Ink("#FF9AAA"), 11);
                    issueHits.Add((new(left - 3, y + 31, Math.Max(10, right - left + 6), 10), issue));
                }
            }
        }
        Label(context, "Signals · current view", 16, SignalsTop - 22, Muted, 12);
        context.DrawLine(new Pen(GridInk, 1), new(0, SignalsTop - 7), new(Bounds.Width, SignalsTop - 7));
        if (model.Selected is { } selected && model.SelectionInGroup)
        {
            var left = Math.Max(LabelWidth, X(selected.Span.Start)); var right = Math.Min(LabelWidth + PlotWidth, X(selected.Span.End));
            if (right > left) context.FillRectangle(Ink("#132B2C"), new(left, SignalsTop, right - left, model.Signals.Count * RowHeight));
        }
        foreach (var (signal, index) in model.Signals.Select((s, i) => (s, i)))
        {
            var y = SignalsTop + index * RowHeight;
            if (y + RowHeight < visibleTop || y > visibleBottom) continue;
            context.FillRectangle(Ink(Probe[signal.Id % 8]), new(15, y + 16, 5, 20));
            Label(context, $"D{signal.Id}", 26, y + 8, Muted, 12);
            using (context.PushClip(new Rect(57, y, 73, RowHeight))) Label(context, signal.Name, 57, y + 7, Text, 13);
            IBrush color = SignalColor(signal.Id);
            using (context.PushClip(new Rect(LabelWidth, y, PlotWidth, RowHeight)))
            {
                var trace = Trace(signal);
                using (context.PushTransform(Matrix.CreateTranslation(0, y)))
                {
                    using (context.PushOpacity(.6))
                    {
                        foreach (var band in trace.Dense) context.FillRectangle(color, band);
                    }
                    context.DrawGeometry(null, new Pen(color, 1.35), trace.Geometry);
                }
                foreach (var unknown in model.Capture.UnknownLevelSpans(signal))
                {
                    var l = Math.Max(LabelWidth, X(unknown.Start)); var r = Math.Min(LabelWidth + PlotWidth, X(unknown.End));
                    if (r <= l) continue;
                    context.FillRectangle(Surface, new(l, y + 12, r - l, 29));
                    context.DrawLine(new Pen(Muted, 1, DashStyle.Dash), new(l, y + 26), new(r, y + 26));
                    // Missing-sample hatching is drawn later. Keep this label in the
                    // uncovered interval so that a gap cannot erase half its text.
                    long labelStart = unknown.Start;
                    foreach (var gap in model.Capture.Gaps)
                        if (gap.Start < unknown.End && gap.End > labelStart)
                            labelStart = Math.Min(unknown.End, gap.End);
                    var labelX = Math.Max(LabelWidth, X(labelStart));
                    if (r - labelX > 100) Label(context, "Unknown level", labelX + 5, y + 12, Muted, 11);
                    // The first observed transition can end at the same level as before a gap.
                    // Draw that edge explicitly without inventing its preceding stable interval.
                    int recovery = signal.LowerBound(unknown.End);
                    if (recovery < signal.Edges.Count && signal.Edges[recovery].Sample == unknown.End && unknown.End >= model.Start && unknown.End <= model.End)
                        context.DrawLine(new Pen(color, 1.35), new(X(unknown.End), y + 16), new(X(unknown.End), y + 35));
                }
            }
        }
        foreach (var gap in model.Capture.Gaps)
        {
            var left = Math.Max(LabelWidth, X(gap.Start)); var right = Math.Min(LabelWidth + PlotWidth, X(gap.End));
            if (right <= left) continue;
            using (context.PushClip(new Rect(left, SignalsTop, right - left, model.Signals.Count * RowHeight)))
            {
                context.FillRectangle(Ink("#19232E"), new(left, SignalsTop, right - left, model.Signals.Count * RowHeight));
                for (double x = left - Height; x < right; x += 12) context.DrawLine(new Pen(GridInk, 1), new(x, Height), new(x + Height, SignalsTop));
                if (right - left > 95) Label(context, "Missing samples", left + 4, SignalsTop + 5, Muted, 11);
            }
        }
        DrawMarker(context, model.A, "A", false); DrawMarker(context, model.B, "B", true);
        var probe = keyboardMode && IsFocused ? model.Probe : null;
        if (probe != null && model.Signals.Any(s => s.Id == probe.Channel))
        {
            var row = SignalsTop + model.Signals.ToList().FindIndex(s => s.Id == probe.Channel) * RowHeight;
            context.DrawRectangle(null, new Pen(Accent, 1, DashStyle.Dash), new(LabelWidth, row + 1, PlotWidth, RowHeight - 2));
            context.DrawLine(new Pen(Accent, 1, DashStyle.Dash), new(X(probe.Sample), row), new(X(probe.Sample), row + RowHeight));
        }
        int? previewChannel = probe?.Channel ?? hoverChannel;
        long previewSample = probe?.Sample ?? hoverSample;
        if (previewChannel is int hc && (model.Armed == null || probe != null) && !down && model.Signals.Any(s => s.Id == hc))
        {
            var measured = model.Capture.MeasureAt(hc, previewSample);
            var y = SignalsTop + model.Signals.ToList().FindIndex(s => s.Id == hc) * RowHeight + 3;
            double labelX = Math.Clamp(X(previewSample), LabelWidth, Math.Max(LabelWidth, Bounds.Width - 430));
            if (measured.Pulse is { } pulse)
            {
                double x1 = Math.Clamp(X(pulse.Start), LabelWidth, LabelWidth + PlotWidth), x2 = Math.Clamp(X(pulse.End), LabelWidth, LabelWidth + PlotWidth);
                context.DrawLine(new Pen(Accent, 1), new(x1, y), new(x2, y));
            }
            if (measured != model.PinnedMeasurement || (probe != null && model.Armed != null))
            {
                var value = probe != null && model.Armed != null
                    ? $"D{hc} · {model.Us(previewSample):0.###} µs · {(probe.IsEdge ? "edge" : "position")} · Enter selects {(model.Armed == 0 ? "A" : "B")}" : $"Preview D{hc} · " + TimingText.Local(measured, model.Capture.SampleRate);
                var preview = Format(value, Ink("#C9F1EA"), 13);
                labelX = Math.Clamp(X(previewSample), LabelWidth, Math.Max(LabelWidth, LabelWidth + PlotWidth - preview.Width - 8));
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
        var handle = Handles.FirstOrDefault(h => h.Endpoint == (name == "A" ? 0 : 1));
        if (handle == null) return;
        context.DrawLine(new Pen(Accent, 1), new(handle.Bounds.Center.X, 40), new(x, 42));
        context.FillRectangle(Ink("#183D3A"), handle.Bounds);
        Label(context, name, handle.Bounds.X + 7, 25, Accent, 11);
    }
    private void DrawOverview(DrawingContext context)
    {
        Label(context, "Group activity", 16, 2, Muted, 12);
        Label(context, "Full capture · " + TimingText.Duration(model.Capture!.SampleCount / (double)model.Capture.SampleRate), 16, 20, Muted, 11);
        context.FillRectangle(Ink("#18242E"), new(LabelWidth, 7, PlotWidth, 33));
        int bins = Math.Max(1, (int)Math.Ceiling(PlotWidth));
        if (!ReferenceEquals(overviewCapture, model.Capture) || !ReferenceEquals(overviewSignals, model.Signals) || activityBins.Length != bins)
        {
            overviewCapture = model.Capture; overviewSignals = model.Signals; activityBins = model.Capture.ActivityBins(overviewSignals, bins);
        }
        for (int i = 0; i < activityBins.Length; i++) if (activityBins[i]) context.FillRectangle(Ink("#6B8796"), new(LabelWidth + i * PlotWidth / bins, 12, PlotWidth / bins, 13));
        foreach (var gap in model.Capture.Gaps)
        {
            double left = LabelWidth + Math.Clamp(gap.Start / (double)model.Capture.SampleCount, 0, 1) * PlotWidth;
            double right = LabelWidth + Math.Clamp(gap.End / (double)model.Capture.SampleCount, 0, 1) * PlotWidth;
            if (right > left) context.DrawRectangle(Ink("#402F32"), new Pen(Muted, 1, DashStyle.Dash), new(Math.Min(left, LabelWidth + PlotWidth - 2), 8, Math.Max(2, right - left), 30));
        }
        foreach (var issue in model.Issues.Where(i => model.IsAllSignals || model.Group.Decoders.Contains(i.Packet.Decoder)))
        {
            double sample = issue.Diagnostic.Spans.Count > 0 ? issue.Diagnostic.Spans.Min(s => s.Start) : issue.Packet.Span.Start;
            double x = LabelWidth + sample / model.Capture.SampleCount * PlotWidth;
            Label(context, "!", Math.Clamp(x - 3, LabelWidth, LabelWidth + PlotWidth - 10), 24, Ink("#FF9AAA"), 12);
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
        Focus(); SetKeyboardMode(false); var p = e.GetPosition(this);
        if (overview) { if (p.X < LabelWidth || p.X > LabelWidth + PlotWidth) return; model.Remember(); var center = Math.Clamp((p.X - LabelWidth) / PlotWidth, 0, 1) * model.Capture.SampleCount; var half = (model.End - model.Start) / 2; model.SetRange(center - half, center + half); e.Handled = true; return; }
        if (p.X < LabelWidth) return;
        if (p.X > LabelWidth + PlotWidth) return;
        press = p; pressStart = model.Start; pressEnd = model.End; pressCapture = model.Capture; down = true; dragged = false; armedOnPress = model.Armed;
        pressedHandle = rulerOnly ? Handles.FirstOrDefault(h => h.Bounds.Contains(p)) : null;
        double distanceA = model.A is { } a ? Math.Abs(X(a.Sample) - p.X) : double.PositiveInfinity;
        double distanceB = model.B is { } b ? Math.Abs(X(b.Sample) - p.X) : double.PositiveInfinity;
        dragEndpoint = armedOnPress != null ? null : pressedHandle?.Endpoint ?? (Math.Min(distanceA, distanceB) > 7 ? null : Math.Abs(distanceA - distanceB) < .01 ? null : distanceA < distanceB ? 0 : 1);
        dragSampleOrigin = (dragEndpoint == 0 ? model.A : model.B)?.Sample ?? 0;
        activePointer = e.Pointer; e.Pointer.Capture(this); e.Handled = true;
    }
    private void Moved(object? sender, PointerEventArgs e)
    {
        var p = e.GetPosition(this); hoverChannel = p.X >= LabelWidth && p.X <= LabelWidth + PlotWidth ? ChannelAt(p.Y) : null; hoverSample = Sample(p.X);
        if (!overview && !rulerOnly)
        {
            var labelChannel = p.X < LabelWidth ? ChannelAt(p.Y) : null;
            var issue = issueHits.FirstOrDefault(h => h.Bounds.Contains(p)).Issue;
            ToolTip.SetTip(this, issue != null ? $"{issue.Diagnostic.Layer} · {issue.Diagnostic.Message}. Click to inspect; use Locate issue for its evidence." : labelChannel is int id && p.X < 134 ? $"D{id} · {model.Signals.First(s => s.Id == id).Name} · Physical probe identity" : null);
        }
        if (!down) { InvalidateVisual(); return; }
        if (!ReferenceEquals(pressCapture, model.Capture)) { down = false; e.Pointer.Capture(null); return; }
        if (armedOnPress != null) { InvalidateVisual(); return; }
        if (!dragged && Math.Abs(p.X - press.X) > 4) { dragged = true; if (dragEndpoint == null) model.Remember(); else model.BeginDrag(); }
        if (!dragged) return;
        if (dragEndpoint is int target)
        {
            var previous = target == 0 ? model.A : model.B; var channel = ChannelAt(p.Y) ?? previous?.Channel;
            long sample = (long)Math.Round(dragSampleOrigin + (p.X - press.X) / PlotWidth * (pressEnd - pressStart));
            if (channel is int id && p.X >= LabelWidth && p.X <= LabelWidth + PlotWidth) model.Pick(id, sample, (model.End - model.Start) / PlotWidth * 8, target);
        }
        else { var delta = (p.X - press.X) / PlotWidth * (pressEnd - pressStart); model.SetRange(pressStart - delta, pressEnd - delta); }
    }
    private void Released(object? sender, PointerReleasedEventArgs e)
    {
        if (!down) return;
        down = false; var p = e.GetPosition(this); var finishedDrag = dragEndpoint; e.Pointer.Capture(null);
        if (!ReferenceEquals(pressCapture, model.Capture)) { dragEndpoint = null; return; }
        if (armedOnPress != null)
        {
            if (model.Armed == armedOnPress && p.X >= LabelWidth && p.X <= LabelWidth + PlotWidth && ChannelAt(p.Y) is int chosen)
                model.Pick(chosen, Sample(p.X), (model.End - model.Start) / PlotWidth * 8);
        }
        else if (dragged && finishedDrag != null) model.FinishDrag();
        else if (!dragged && pressedHandle != null) EndpointRequested?.Invoke(pressedHandle.Endpoint);
        else if (!dragged)
        {
            var issue = issueHits.FirstOrDefault(h => h.Bounds.Contains(p)).Issue;
            var hit = hits.FirstOrDefault(h => h.Bounds.Contains(p));
            if (issue != null) model.SelectIssue(issue);
            else if (hit.Packet != null) model.Select(hit.Packet);
            else if (ChannelAt(p.Y) is int channel) model.Pick(channel, Sample(p.X), (model.End - model.Start) / PlotWidth * 8);
        }
        dragEndpoint = null; e.Handled = true;
    }
}
