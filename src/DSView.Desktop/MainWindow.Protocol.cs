// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using DSView.Domain;

namespace DSView.Desktop;

public sealed partial class MainWindow
{
    private int bytePage;
    private const int BytePageSize = 64;
    private readonly Dictionary<int, Button> byteButtons = [];
    private Button? packetContentFocus;

    private void RenderProtocolDetails(Packet packet)
    {
        byteButtons.Clear();
        var items = Model.Capture!.ProtocolItems(packet);
        var mapped = items.Select((item, index) => (item, index)).Where(p => p.item.DataIndex != null).ToDictionary(p => p.item.DataIndex!.Value, p => p.index);
        if (Model.SelectedProtocol is { } selected)
        {
            var detail = new StackPanel { Spacing = 6 };
            string identity = selected.Identity;
            if (selected.Kind == "ack" && Model.SelectedProtocolIndex is > 0 and int ordinal)
            {
                var preceding = items[ordinal - 1];
                if (preceding.Span.End <= selected.Span.Start && preceding.DataIndex is int b) identity += $" · Data byte {b + 1}";
                else if (preceding.Span.End <= selected.Span.Start && preceding.IsAddress) identity += " · Address";
            }
            var markerStyle = ProtocolMarkerStyle.For(selected);
            var label = Text(identity); label.FontWeight = FontWeight.SemiBold; label.TextWrapping = TextWrapping.Wrap;
            if (markerStyle != null) label.Foreground = markerStyle.Foreground;
            detail.Children.Add(label);
            var span = Text(selected.Kind == "point"
                ? TimeAxis.Microseconds(selected.Span.Start, Model.Capture.SampleRate) + " µs · event"
                : TimeAxis.FormatRange(selected.Span.Start, selected.Span.End, Model.Capture.SampleRate) + " · decoded interval", true);
            span.TextWrapping = TextWrapping.Wrap;
            ToolTip.SetTip(span, $"Samples {selected.Span.Start}–{selected.Span.End}. Decoder annotations are not raw-edge timing measurements."); detail.Children.Add(span);
            if (selected.Span.End > Model.Capture.SampleCount || selected.Span.Start >= Model.Capture.SampleCount)
                detail.Children.Add(Wrapped("Annotation reaches beyond available raw samples.", true));
            else if (Model.Capture.Gaps.Any(g => (selected.Span.Start == selected.Span.End ? g.Start <= selected.Span.Start : g.Start < selected.Span.End) && g.End > selected.Span.Start))
                detail.Children.Add(Wrapped("Missing samples overlap this annotation.", true));
            var actions = new WrapPanel { Orientation = Orientation.Horizontal };
            actions.Children.Add(Button("Locate selected", Model.Locate, "flat"));
            actions.Children.Add(Button("Whole packet", () => { Model.Select(packet); packetContentFocus?.Focus(); }, "flat"));
            if (selected.DataIndex is int dataIndex)
            {
                actions.Children.Add(CopyButton("Copy byte", packet.Bytes[dataIndex].ToString("X2")));
                bytePage = dataIndex / BytePageSize;
            }
            detail.Children.Add(actions);
            inspector.Children.Add(new Border { BorderBrush = TimelineControl.Ink("#78D7C8"), BorderThickness = new(2, 0, 0, 0), Padding = new(10, 2), Child = detail });
        }
        if (packet.Bytes.Length == 0) return;
        var header = new Grid { ColumnDefinitions = new("*,Auto") };
        packetContentFocus = CopyButton("Copy data", packet.Hex);
        Add(header, Text("Data bytes · Hex", true), 0, 0); Add(header, packetContentFocus, 0, 1); inspector.Children.Add(header);
        int start = Math.Clamp(bytePage * BytePageSize, 0, packet.Bytes.Length - 1), end = Math.Min(packet.Bytes.Length, start + BytePageSize);
        var bytes = new WrapPanel { Orientation = Orientation.Horizontal };
        for (int i = start; i < end; i++)
        {
            int dataIndex = i;
            if (mapped.TryGetValue(i, out int protocolIndex))
            {
                var button = Button(packet.Bytes[i].ToString("X2"), () =>
                {
                    Model.SelectProtocol(packet, protocolIndex);
                    if (byteButtons.TryGetValue(dataIndex, out var current)) current.Focus();
                }, "flat", $"Data byte {i + 1} · select decoded source");
                button.MinWidth = 34; button.Padding = new(6, 5); button.Margin = new(0, 0, 2, 2); button.FontFamily = new("Cascadia Mono,Consolas"); button.Bind(FontSizeProperty, new DynamicResourceExtension("DataFontSize"));
                AutomationProperties.SetName(button, $"Select data byte {i + 1} · 0x{packet.Bytes[i]:X2}");
                if (Model.SelectedProtocolIndex == protocolIndex) { button.Background = TimelineControl.Ink("#245A53"); button.BorderBrush = TimelineControl.Ink("#78D7C8"); button.BorderThickness = new(0, 0, 0, 2); }
                byteButtons.Add(i, button); bytes.Children.Add(button);
            }
            else
            {
                var value = new SelectableTextBlock { Text = packet.Bytes[i].ToString("X2"), FontFamily = new("Cascadia Mono,Consolas"), Margin = new(6, 5, 8, 7) };
                value.Bind(TextBlock.FontSizeProperty, new DynamicResourceExtension("DataFontSize")); bytes.Children.Add(value);
            }
        }
        inspector.Children.Add(bytes);
        if (byteButtons.Count > 0) packetContentFocus = byteButtons.Values.First();
        if (mapped.Count < packet.Bytes.Length) inspector.Children.Add(Wrapped("Individual byte sources are unavailable for this result.", true));
        if (packet.Bytes.Length > BytePageSize)
        {
            var paging = Row(); paging.Children.Add(Text($"Bytes {start + 1}–{end} of {packet.Bytes.Length}", true));
            void Page(int delta)
            {
                bytePage += delta;
                if (Model.SelectedProtocol == null) RenderInspector(); else Model.Select(packet);
                packetContentFocus?.Focus();
            }
            var previous = Button("‹", () => Page(-1), "flat", "Previous bytes"); previous.IsEnabled = start > 0;
            var next = Button("›", () => Page(1), "flat", "Next bytes"); next.IsEnabled = end < packet.Bytes.Length;
            paging.Children.Add(previous); paging.Children.Add(next); inspector.Children.Add(paging);
        }
    }
    private static TextBlock Wrapped(string value, bool muted) { var block = Text(value, muted); block.TextWrapping = TextWrapping.Wrap; return block; }
    private Button CopyButton(string label, string value)
    {
        var result = Button("", () => { }, "flat", label);
        result.Content = ActionIcons.Create(ActionIcons.Copy);
        result.Width = result.Height = 32; result.Padding = new(7);
        AutomationProperties.SetName(result, label);
        void Feedback(bool success, string message)
        {
            result.Content = ActionIcons.Create(success ? ActionIcons.Check : ActionIcons.Retry);
            ToolTip.SetTip(result, label + " · " + message);
            AutomationProperties.SetName(result, label + " · " + message);
        }
        result.Click += async (_, _) =>
        {
            try
            {
                if (Clipboard == null) { Feedback(false, "Clipboard unavailable"); return; }
                await Clipboard.SetTextAsync(value); await Clipboard.FlushAsync(); Feedback(true, "Copied");
            }
            catch { Feedback(false, "Copy failed · click to retry"); }
        };
        return result;
    }
}
