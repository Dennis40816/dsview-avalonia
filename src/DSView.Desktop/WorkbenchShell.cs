// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Layout;
using CommunityToolkit.Mvvm.Input;

namespace DSView.Desktop;

// UI thread only. Owns sidebar presentation; it has no capture or analysis state.
internal sealed class WorkbenchShell
{
    private enum LayoutMode { WideOpen, WideClosed, CompactOpen, CompactClosed }
    private LayoutMode _mode = LayoutMode.WideOpen;
    private readonly Border _sidebar = new() { Background = TimelineControl.Ink("#141F29"), BorderBrush = TimelineControl.Ink("#26343F"), BorderThickness = new(0, 0, 1, 0) };
    private readonly Button _close;
    public Grid Header { get; } = new() { ColumnDefinitions = new("Auto,*,Auto,Auto"), Margin = new(16, 0) };
    public Grid Body { get; } = new() { ColumnDefinitions = new("220,*") };
    public TextBlock ActiveGroup { get; } = new() { TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis, Margin = new(16, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = TimelineControl.Ink("#9BAEBF") };
    public Button Toggle { get; }
    public ListBox Groups { get; } = new() { Background = Avalonia.Media.Brushes.Transparent, BorderThickness = new(0) };
    public Button Activity { get; }
    public Button Results { get; }
    public Button Issues { get; }
    public Button Settings { get; }
    public Button Decoders { get; }
    public EdgeNavigationFlyout? Edges { get; private set; }
    private bool IsCompact => _mode is LayoutMode.CompactOpen or LayoutMode.CompactClosed;
    private bool IsOpen => _mode is LayoutMode.WideOpen or LayoutMode.CompactOpen;

    public WorkbenchShell(Action activity, Action results, Action issues, Action settings, Action decoders)
    {
        Toggle = ActionIcons.Button("Show or hide Groups", ActionIcons.Groups, new RelayCommand(ToggleSidebar));
        _close = ActionIcons.Button("Close Groups", ActionIcons.Close, new RelayCommand(() => { _mode = LayoutMode.CompactClosed; Render(); Toggle.Focus(); }));
        Body.Children.Add(_sidebar);
        Activity = TextAction("Activity", activity); Results = TextAction("Results", results); Issues = TextAction("Capture issues", issues);
        Issues.Foreground = TimelineControl.Ink("#FF9AAA");
        Settings = ActionIcons.Button("Settings", ActionIcons.Settings, new RelayCommand(settings));
        Decoders = TextAction("Decoders", decoders);
    }
    public void SetGroupContents(Action create, Action edit)
    {
        var body = new Grid { RowDefinitions = new("Auto,*,Auto"), Margin = new(10, 12) };
        var heading = new Grid { ColumnDefinitions = new("*,Auto,Auto,Auto"), Margin = new(6, 0, 0, 8) };
        Add(heading, new TextBlock { Text = "Groups", FontWeight = Avalonia.Media.FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center }, 0);
        Add(heading, ActionIcons.Button("New Group", ActionIcons.Add, new RelayCommand(create)), 0, 1);
        Add(heading, ActionIcons.Button("Edit current Group", ActionIcons.Edit, new RelayCommand(edit)), 0, 2);
        Add(heading, _close, 0, 3); Add(body, heading, 0); Add(body, Groups, 1);
        var context = new StackPanel { Spacing = 6, Margin = new(0, 12, 0, 0) };
        context.Children.Add(new Separator());
        foreach (var button in new[] { Decoders, Activity, Results, Issues })
        {
            button.HorizontalAlignment = HorizontalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Left;
            context.Children.Add(button);
        }
        Add(body, context, 2); _sidebar.Child = body;
    }
    public void SetEdges(Control channel, Control previous, Control next, Action<Flyout> activate) => Edges = new(channel, previous, next, activate);
    private static Button TextAction(string text, Action action)
    {
        var button = new Button { Content = text, Command = new RelayCommand(action), VerticalAlignment = VerticalAlignment.Center };
        button.Classes.Add("flat"); return button;
    }
    public void DismissCompact()
    {
        if (!IsCompact) return;
        _mode = LayoutMode.CompactClosed; Render();
    }
    public void SetWidth(double width)
    {
        if (width < 1280 && !IsCompact) _mode = LayoutMode.CompactClosed;
        else if (width >= 1280 && IsCompact) _mode = LayoutMode.WideOpen;
        Render();
    }
    private void ToggleSidebar()
    {
        _mode = _mode switch { LayoutMode.WideOpen => LayoutMode.WideClosed, LayoutMode.WideClosed => LayoutMode.WideOpen, LayoutMode.CompactOpen => LayoutMode.CompactClosed, _ => LayoutMode.CompactOpen };
        Render();
    }
    private void Render()
    {
        _sidebar.IsVisible = IsOpen; _close.IsVisible = IsCompact;
        Body.ColumnDefinitions[0].Width = new(!IsCompact && IsOpen ? 220 : 0);
        Grid.SetColumn(_sidebar, IsCompact ? 1 : 0);
        _sidebar.HorizontalAlignment = IsCompact ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
        _sidebar.Width = IsCompact ? 240 : double.NaN;
        ActiveGroup.IsVisible = !IsOpen || IsCompact;
        ToolTip.SetTip(Toggle, IsOpen ? "Hide Groups" : "Show Groups");
    }
    private static void Add(Grid grid, Control child, int row, int column = 0)
    { Grid.SetRow(child, row); Grid.SetColumn(child, column); grid.Children.Add(child); }
}
