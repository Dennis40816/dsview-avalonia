// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using DSView.Domain;
using DSView.Infrastructure;

namespace DSView.Desktop;

public sealed partial class MainWindow : Window
{
    public Workbench Model { get; } = new();
    // New feature owners are UI-thread confined; legacy window state is tracked separately.
    private readonly WorkbenchShell _shell;
    private readonly DecoderViewModel _decoder;
    private readonly DecoderView _decoderView;
    private readonly SettingsView _settingsView = new(new LocalAppearanceStore()) { IsVisible = false };
    private readonly ComboBox modePicker = new() { ItemsSource = new[] { "Snap to edge", "Position" }, SelectedIndex = 0, Width = 134 };
    private readonly ComboBox edgeChannel = new() { Width = 74 };
    private readonly TextBlock range = Text(""), status = Text("Loading…", true), hint = Text("", true), delta = Text("");
    private readonly TextBox aTime = new() { Width = 95 }, bTime = new() { Width = 95 };
    private readonly TextBlock aSource = Text("A", true), bSource = Text("B", true);
    private readonly TextBlock packetContext = Text("", true);
    private readonly TextBlock measurementTitle = Text("", true);
    private readonly TextBlock guide = Text("Wheel to zoom · Shift + wheel to scroll signals", true);
    private readonly TextBlock waveEmptyText = Text("");
    private readonly StackPanel waveEmpty = new() { Spacing = 12, Margin = new(24, 28), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly Button waveEmptyAction, previousEdgeButton, nextEdgeButton;
    private readonly List<Button> endpointPickButtons = [];
    private readonly List<Button> endpointShowButtons = [];
    private readonly WrapPanel endpoints = new() { Orientation = Orientation.Horizontal };
    private readonly StackPanel inspector = new() { Spacing = 14, Margin = new Thickness(16) };
    private readonly StackPanel measurement = new() { Spacing = 6, Margin = new(16, 4) };
    private readonly StackPanel measurementActions = Row();
    private readonly Grid navigation = new() { ColumnDefinitions = new("Auto,Auto,*,Auto,Auto"), Margin = new(12, 0), MinHeight = 38 };
    private readonly TimelineControl overview, timeline, ruler;
    private readonly Dictionary<TextBox, string> invalidTimes = [];
    private readonly Grid workspace = new() { ColumnDefinitions = new("*,0,0") };
    private readonly ListBox packets = new() { FontFamily = new("Cascadia Mono,Consolas"), Background = Brushes.Transparent };
    private readonly TextBox search = new() { PlaceholderText = "Search packets, Hex or fields", Margin = new(14, 8) };
    private readonly StackPanel packetEmpty = new() { Spacing = 12, Margin = new(24, 28), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly TextBlock packetEmptyText = Text("");
    private readonly Button packetEmptyAction;
    private PacketEmptyReason packetEmptyReason;
    private readonly Button waveTab, packetTab, detailButton, reloadButton, measureButton, keepResultButton;
    private readonly ScrollViewer waveScroll;
    private DemoExample activeExample = DemoCaptureSource.Examples[0];
    private readonly Control wavePanel, packetPanel;
    private readonly Border inspectorBorder;
    private readonly GridSplitter splitter = new() { Width = 4, Background = TimelineControl.Ink("#26343F"), ResizeDirection = GridResizeDirection.Columns };
    private CancellationTokenSource? loadCancellation;
    private int generation;
    private int revealGeneration;
    private bool updating;
    private Packet? renderedPacket;
    private CaptureIssue? renderedIssue;
    private int? renderedProtocolIndex;
    private Flyout? openPanel;
    private readonly string preferencePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DSView2", "workbench.json");
    public MainWindow()
    {
        Title = "DSView2 · Offline demo"; Width = 1380; Height = 850; MinWidth = 1024; MinHeight = 650;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        _decoder = new(Model);
        _decoderView = new() { DataContext = _decoder, IsVisible = false };
        _shell = new(ShowActivity, () => _ = ShowTimingResultsAsync(), ShowIssues, ShowSettings, ShowDecoders);
        RestorePreferences();
        workspace.ColumnDefinitions[0].MinWidth = 480;
        var page = new Grid { RowDefinitions = new("52,*") };
        var header = _shell.Header;
        var brand = Text("DSView2"); brand.FontWeight = FontWeight.SemiBold; brand.Margin = new(0, 0, 20, 0);
        Add(header, brand, 0);
        var source = Button("", ShowInfo, "flat", "Capture source and acquisition details"); source.Content = status; source.HorizontalAlignment = HorizontalAlignment.Left; Add(header, source, 0, 1);
        reloadButton = Button("File ▾", ShowFiles, "flat", "Import a capture, or open and save an analysis"); Add(header, reloadButton, 0, 2);
        Add(header, _shell.Settings, 0, 3);
        status.MaxWidth = 560; status.TextTrimming = TextTrimming.CharacterEllipsis;
        Add(page, Band(header), 0);
        waveTab = Button("Waveforms", () => Model.SetTab(false), "flat"); packetTab = Button("Packets", () => Model.SetTab(true), "flat"); waveTab.Classes.Add("tab"); packetTab.Classes.Add("tab");
        detailButton = IconButton("Packet details", ActionIcons.Details, Model.ToggleDetails);
        measureButton = Button("Cursor A/B", () => { Model.SetMode(1); timeline!.FocusForPick(); }, "flat", "Drag from A to B, or click two endpoints. Then drag either cursor to refine it.");
        _shell.Groups.ItemsSource = Model.Groups; _shell.Groups.SelectedItem = Model.Group;
        _shell.Groups.ItemTemplate = new FuncDataTemplate<ObservationGroup>((group, _) => { var text = Text(group?.Name ?? ""); text.TextTrimming = TextTrimming.CharacterEllipsis; ToolTip.SetTip(text, group?.Name); return text; });
        AutomationProperties.SetName(_shell.Groups, "Signal Groups");
        _shell.Groups.SelectionChanged += (_, _) => { if (!updating && _shell.Groups.SelectedItem is ObservationGroup group) { Model.SetGroup(group); _shell.DismissCompact(); } };
        _shell.SetGroupContents(() => EditGroup(true), () => EditGroup(false));
        var center = new Grid { RowDefinitions = new("44,Auto,Auto,*") };
        var tabs = new Grid { ColumnDefinitions = new("Auto,Auto,Auto,*,Auto,Auto"), Margin = new(12, 0) };
        Add(tabs, _shell.Toggle, 0); Add(tabs, waveTab, 0, 1); Add(tabs, packetTab, 0, 2);
        _shell.ActiveGroup.TextTrimming = TextTrimming.CharacterEllipsis; _shell.ActiveGroup.Margin = new(16, 0); Add(tabs, _shell.ActiveGroup, 0, 3);
        Add(tabs, measureButton, 0, 4); Add(tabs, detailButton, 0, 5); Add(center, Band(tabs), 0);
        var nav = navigation;
        Add(nav, IconButton("Previous view", ActionIcons.Back, Model.Previous), 0);
        Add(nav, IconButton("Fit full capture", ActionIcons.Fit, Model.FitTime), 0, 1);
        range.Margin = new(14, 0); range.TextTrimming = TextTrimming.CharacterEllipsis; Add(nav, range, 0, 2);
        edgeChannel.Margin = new(4, 0); ToolTip.SetTip(edgeChannel, "Signal for previous / next edge"); AutomationProperties.SetName(edgeChannel, "Edge navigation signal");
        edgeChannel.SelectionChanged += (_, _) => { if (!updating) ScheduleTitleUpdate(); };
        previousEdgeButton = Button("‹", () => Model.StepEdge(SelectedEdgeChannel(), -1), "flat", "Previous edge");
        nextEdgeButton = Button("›", () => Model.StepEdge(SelectedEdgeChannel(), 1), "flat", "Next edge");
        _shell.SetEdges(edgeChannel, previousEdgeButton, nextEdgeButton, flyout => { openPanel?.Hide(); openPanel = flyout; });
        Add(nav, _shell.Edges!.Toggle, 0, 3);
        guide.HorizontalAlignment = HorizontalAlignment.Right; guide.Margin = new(12, 0, 0, 0); Add(nav, guide, 0, 4); Add(center, nav, 1);
        overview = new TimelineControl(Model, true); Add(center, overview, 2);
        timeline = new TimelineControl(Model); timeline.ColorRequested += EditColor;
        timeline.EditingCancelled += CancelEditing;
        timeline.KeyboardModeChanged += enabled => guide.Text = enabled ? "↑↓ signal · ←→ edge / sample · Enter select · Esc cancel" : "Wheel to zoom · Shift + wheel to scroll signals";
        Model.SignalFocusRequested += channel => QueueSignalReveal([channel]);
        Model.MeasurementFocusRequested += QueueSignalReveal;
        ruler = new TimelineControl(Model, isRuler: true) { HorizontalAlignment = HorizontalAlignment.Left };
        ruler.EndpointRequested += index => { if (Model.Signals.Count > 0) { Model.Arm(index); timeline.FocusForPick(); } };
        ruler.EditingCancelled += CancelEditing;
        timeline.SizeChanged += (_, _) => UpdateRulerWidth();
        var waveformGrid = new Grid { RowDefinitions = new("42,Auto,*") };
        Add(waveformGrid, ruler, 0);
        var timelineHost = new Grid(); timelineHost.Children.Add(timeline); timelineHost.Children.Add(timeline.LabelControls);
        waveScroll = new ScrollViewer { Content = timelineHost, VerticalContentAlignment = VerticalAlignment.Top, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        void ScrollRows(double delta) => waveScroll.Offset = new(0, Math.Clamp(waveScroll.Offset.Y + delta, 0, Math.Max(0, waveScroll.Extent.Height - waveScroll.Viewport.Height)));
        timeline.ScrollRowsRequested += ScrollRows; ruler.ScrollRowsRequested += ScrollRows;
        waveScroll.ScrollChanged += (_, _) => { timeline.SetViewport(waveScroll.Offset.Y, waveScroll.Viewport.Height); UpdateMeasurementContext(); ScheduleTitleUpdate(); };
        Add(waveformGrid, waveScroll, 2);
        waveEmptyText.TextWrapping = TextWrapping.Wrap; waveEmptyText.MaxWidth = 420;
        waveEmptyAction = Button("", () =>
        {
            if (Model.Capture?.Signals.Count > 0) { Model.ShowAll(); waveTab.Focus(); }
            else ShowInfo();
        }, "flat");
        waveEmptyAction.HorizontalAlignment = HorizontalAlignment.Left;
        waveEmpty.Children.Add(waveEmptyText); waveEmpty.Children.Add(waveEmptyAction); Add(waveformGrid, waveEmpty, 2);
        wavePanel = waveformGrid;
        wavePanel.SizeChanged += (_, _) => UpdateRulerWidth();
        var packetGrid = new Grid { RowDefinitions = new("Auto,*") }; Add(packetGrid, search, 0); Add(packetGrid, packets, 1); packetPanel = packetGrid;
        packetEmptyText.TextWrapping = TextWrapping.Wrap; packetEmptyText.MaxWidth = 420;
        packetEmptyAction = Button("", RecoverPackets, "flat"); packetEmptyAction.HorizontalAlignment = HorizontalAlignment.Left;
        packetEmpty.Children.Add(packetEmptyText); packetEmpty.Children.Add(packetEmptyAction); Add(packetGrid, packetEmpty, 1);
        packets.ItemTemplate = new FuncDataTemplate<Packet>((packet, _) => { var text = Text(packet == null ? "" : TimingText.Packet(packet, Model.Capture!.SampleRate)); text.Bind(TextBlock.FontSizeProperty, new DynamicResourceExtension("DataFontSize")); return text; });
        search.TextChanged += (_, _) => { if (!updating) Model.Search(search.Text ?? ""); };
        packets.SelectionChanged += (_, _) => { if (!updating && packets.SelectedItem is Packet packet) Model.Select(packet); };
        var work = new Grid(); work.Children.Add(wavePanel); work.Children.Add(packetPanel); Add(workspace, work, 0, 0); Add(workspace, splitter, 0, 1);
        inspectorBorder = new Border { BorderBrush = TimelineControl.Ink("#26343F"), BorderThickness = new(1, 0, 0, 0), Child = new ScrollViewer { Content = inspector, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } };
        Add(workspace, inspectorBorder, 0, 2); Add(workspace, _decoderView, 0, 2); Add(center, workspace, 3);
        Grid.SetColumn(center, 1); _shell.Body.Children.Insert(0, center);
        Add(page, _shell.Body, 1); Add(page, _settingsView, 1);
        SizeChanged += (_, _) => UpdateShellLayout();
        var result = new Grid { ColumnDefinitions = new("Auto,*"), RowDefinitions = new("Auto,Auto") };
        measurementTitle.TextTrimming = TextTrimming.CharacterEllipsis; measurementTitle.MaxWidth = 152; measurementTitle.Margin = new(0, 0, 14, 0); Add(result, measurementTitle, 0, 0);
        delta.Foreground = TimelineControl.Ink("#C9F1EA"); delta.FontFamily = new("Cascadia Mono,Consolas"); delta.Bind(TextBlock.FontSizeProperty, new DynamicResourceExtension("DataLargeFontSize")); delta.TextWrapping = TextWrapping.Wrap; Add(result, delta, 0, 1);
        measurementActions.Spacing = 6; measurementActions.Margin = new(0, 4, 0, 0);
        measurementActions.Children.Add(modePicker); measurementActions.Children.Add(Button("Cancel", CancelEditing, "flat"));
        measurementActions.Children.Add(Button("Fit", Model.FitMarkers, "flat", "Fit current measurement evidence, including its complete cycle, and reveal source rows")); measurementActions.Children.Add(Button("Clear", Model.Clear, "flat", "Clear measurement")); Add(result, measurementActions, 1); Grid.SetColumnSpan(measurementActions, 2);
        keepResultButton = Button("Keep result", KeepTimingResult, "flat", "Keep a copy of this measurement in the analysis"); measurementActions.Children.Add(keepResultButton);
        modePicker.SelectionChanged += (_, _) => { if (!updating) Model.SetSnapMode(modePicker.SelectedIndex == 1 ? 2 : 1); };
        foreach (var (label, box, index) in new[] { (aSource, aTime, 0), (bSource, bTime, 1) })
        {
            var endpoint = new Grid { ColumnDefinitions = new("100,95,Auto,Auto"), Width = 308, Margin = new(0, 0, 16, 0) };
            box.VerticalAlignment = VerticalAlignment.Center;
            var endpointSource = new StackPanel(); label.TextWrapping = TextWrapping.Wrap; label.Margin = new(0, 0, 8, 0); endpointSource.Children.Add(label);
            var show = Button(index == 0 ? "Show A" : "Show B", () => { Model.ShowEndpoint(index); waveTab.Focus(); }, "flat", "Reveal this endpoint without changing its value");
            show.HorizontalAlignment = HorizontalAlignment.Left; endpointSource.Children.Add(show); endpointShowButtons.Add(show);
            Add(endpoint, endpointSource, 0, 0); Add(endpoint, box, 0, 1);
            var units = Text("µs", true); units.Margin = new(8, 0, 4, 0); Add(endpoint, units, 0, 2);
            var choose = Button("Choose", () => { Model.Arm(index); timeline.FocusForPick(); }, "flat", "Replace only this endpoint");
            endpointPickButtons.Add(choose);
            AutomationProperties.SetName(choose, $"Choose {(index == 0 ? "A" : "B")} endpoint"); AutomationProperties.SetName(box, $"{(index == 0 ? "A" : "B")} time in microseconds");
            ToolTip.SetTip(box, "Typing sets a Position on the sample grid. Enter commits; Esc cancels.");
            box.GotFocus += (_, _) => { hint.Text = "Typing sets Position · Enter commits · Esc cancels"; hint.IsVisible = true; };
            Add(endpoint, choose, 0, 3); endpoints.Children.Add(endpoint);
        }
        aTime.KeyDown += (_, e) => { if (e.Key == Key.Enter) CommitTime(0, aTime); }; bTime.KeyDown += (_, e) => { if (e.Key == Key.Enter) CommitTime(1, bTime); };
        aTime.LostFocus += (_, _) => CommitTime(0, aTime); bTime.LostFocus += (_, _) => CommitTime(1, bTime);
        hint.TextWrapping = TextWrapping.Wrap; measurement.Children.Add(result); measurement.Children.Add(endpoints); measurement.Children.Add(hint); Add(waveformGrid, Band(measurement), 1);
        WireAppearance(); Content = page; Model.Changed += Refresh;
        _decoder.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(DecoderViewModel.IsOpen)) Refresh(); };
        Model.Changed += ScheduleTitleUpdate;
        titleUpdate.Tick += (_, _) => { titleUpdate.Stop(); UpdateAnalysisTitle(); };
        aTime.TextChanged += (_, _) => ScheduleTitleUpdate(); bTime.TextChanged += (_, _) => ScheduleTitleUpdate();
        Opened += async (_, _) => await LoadAsync(activeExample); Closing += ClosingAnalysis;
        Closed += (_, _) => { _decoder.Dispose(); titleUpdate.Stop(); loadCancellation?.Cancel(); ++generation; };
        KeyDown += (_, e) =>
        {
            if (_settingsView.Model.IsOpen) { if (e.Key == Key.Escape && _settingsView.Model.CancelCommand.CanExecute(null)) { _settingsView.Model.CancelCommand.Execute(null); e.Handled = true; } return; }
            if (openingFile) { if (e.Key == Key.Escape) { loadCancellation?.Cancel(); e.Handled = true; } return; }
            if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.O) { e.Handled = true; _ = OpenAnalysisAsync(); return; }
            if (e.Key == Key.S && (e.KeyModifiers == KeyModifiers.Control || e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift)))
            { e.Handled = true; _ = SaveAnalysisAsync(e.KeyModifiers.HasFlag(KeyModifiers.Shift)); return; }
            if (e.Key != Key.Escape) return;
            if (Model.Armed != null || aTime.IsFocused || bTime.IsFocused) { CancelEditing(); e.Handled = true; }
            else if (_decoder.IsOpen) { _decoder.Close(); e.Handled = true; }
            else if (Model.Details) Model.ToggleDetails();
        };
        Refresh(); UpdateShellLayout();
    }
    private void WireAppearance()
    {
        void RefreshAppearance()
        {
            var value = _settingsView.Model.Current; AppearanceResources.Apply(value);
            timeline.ApplyAppearance(value); ruler.ApplyAppearance(value); overview.ApplyAppearance(value);
            _settingsView.IsVisible = _settingsView.Model.IsOpen; _shell.Body.IsEnabled = !_settingsView.Model.IsOpen && !openingFile;
            foreach (var child in _shell.Header.Children)
                child.IsEnabled = !_settingsView.Model.IsOpen && (!openingFile || ReferenceEquals(child, reloadButton)) || ReferenceEquals(child, _shell.Settings);
        }
        _settingsView.Model.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(AppearanceSettingsViewModel.Current)) RefreshAppearance(); };
        RefreshAppearance();
    }
    private void ShowSettings()
    {
        if (fileBusy) return;
        _decoder.Close();
        openPanel?.Hide(); openPanel = null;
        var command = _settingsView.Model.IsOpen ? _settingsView.Model.CancelCommand : _settingsView.Model.OpenCommand;
        if (command.CanExecute(null)) command.Execute(null);
    }
    private void ShowDecoders()
    {
        if (fileBusy || _settingsView.Model.IsOpen) return;
        openPanel?.Hide(); _shell.DismissCompact();
        if (_decoder.IsOpen) _decoder.Close(); else _decoder.Open();
    }
    private void UpdateShellLayout()
    {
        _shell.SetWidth(Bounds.Width > 0 ? Bounds.Width : Width);
        guide.IsVisible = Model.Signals.Count > 0 && Bounds.Width >= 1450;
    }
    private static Button IconButton(string label, string path, Action action) => ActionIcons.Button(label, path, new RelayCommand(action));
    private int SelectedEdgeChannel() => edgeChannel.SelectedItem is string str && int.TryParse(str.AsSpan(1), out var id) ? id : Model.Group.Channels.FirstOrDefault();
    private void UpdateRulerWidth() => ruler.Width = Model.Signals.Count > 0 ? timeline.Bounds.Width : wavePanel.Bounds.Width;
    private string CurrentSourceName => string.IsNullOrEmpty(sessionSourceName) ? activeExample.Name : sessionSourceName;
    private string SourceSummary(Capture capture) => $"{(analysisPath != null ? "Saved analysis · " + Path.GetFileName(analysisPath) : importedPath != null ? "Imported · " + CurrentSourceName : "Offline · " + activeExample.Name)} · {capture.SampleRate / 1e6:0.##} MS/s · {TimingText.Duration(capture.SampleCount / (double)capture.SampleRate)}" + (capture.Gaps.Count > 0 ? " · Missing data" : "") + " ▾";
    private async Task LoadAsync(DemoExample example)
    {
        _decoder.Close();
        if (fileBusy || !await ConfirmReplacementAsync()) return;
        loadCancellation?.Cancel(); loadCancellation?.Dispose(); loadCancellation = new(); int current = ++generation;
        string showing = Model.Capture is { } loaded ? SourceSummary(loaded) : "No capture";
        status.Text = showing + $" · Loading {example.Name}…"; fileBusy = true; SetOpening(true);
        if (packetEmptyReason == PacketEmptyReason.NoCapture) packetEmptyAction.IsEnabled = false;
        try
        {
            var capture = await DemoCaptureSource.LoadAsync(example.Id, loadCancellation.Token);
            if (current == generation)
            {
                renderedPacket = null; renderedIssue = null; ResetNumericErrors();
                RestorePersonalSettings(); analysisPath = null; importedPath = null; sessionSourceName = example.Name;
                Model.Load(capture); activeExample = example; waveScroll.Offset = default;
                baselineCapture = capture; baselineState = AnalysisFile.StateKey(CurrentAnalysisState());
                UpdateSourceIdentity();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (current == generation) await FileMessageAsync("Open failed", "Current analysis was kept.\n" + ex.Message); }
        finally { if (current == generation) { fileBusy = false; SetOpening(false); packetEmptyAction.IsEnabled = true; UpdateSourceIdentity(); } }
    }
    private void Refresh()
    {
        // Any subsequent model/edit refresh supersedes a queued navigation request.
        ++revealGeneration;
        if (updating) return; updating = true;
        try
        {
            if (!ReferenceEquals(openPanel, _shell.Edges)) { openPanel?.Hide(); openPanel = null; }
            _shell.Groups.SelectedItem = Model.Group; _shell.ActiveGroup.Text = Model.Group.Name; _shell.Activity.Content = $"Activity · {Model.Capture?.Signals.Count ?? 0} signals";
            AutomationProperties.SetName(_shell.Activity, $"Activity · {Model.Signals.Count} visible of {Model.Capture?.Signals.Count ?? 0} captured signals");
            _shell.Issues.Content = $"Capture issues · {Model.Issues.Count}"; _shell.Issues.IsVisible = Model.Issues.Count > 0;
            _shell.Results.Content = $"Results · {Model.Measurements.Count}"; _shell.Results.IsVisible = Model.Measurements.Count > 0 || Model.CanUndoMeasurementRemoval;
            keepResultButton.IsVisible = Model.A != null && Model.B != null && !Model.HasPendingEdit;
            range.Text = Model.RangeText;
            if (search.Text != Model.Query) search.Text = Model.Query;
            var old = edgeChannel.SelectedItem as string; var choices = Model.Signals.Select(c => $"D{c.Id}").ToArray();
            if (!(edgeChannel.ItemsSource?.Cast<string>().SequenceEqual(choices) ?? false)) { edgeChannel.ItemsSource = choices; edgeChannel.SelectedItem = choices.Contains(old) ? old : choices.FirstOrDefault(); }
            wavePanel.IsVisible = !Model.PacketsTab; packetPanel.IsVisible = Model.PacketsTab;
            navigation.IsVisible = overview.IsVisible = measureButton.IsVisible = !Model.PacketsTab;
            bool hasSignals = Model.Signals.Count > 0;
            waveScroll.IsVisible = hasSignals; waveEmpty.IsVisible = Model.Capture != null && !hasSignals;
            UpdateRulerWidth();
            waveEmptyText.Text = Model.Capture?.Signals.Count == 0 ? "This capture contains no signals."
                : "This Group has no signals in the current capture.";
            waveEmptyAction.Content = Model.Capture?.Signals.Count > 0 ? "Show all signals" : "Choose source";
            measureButton.IsEnabled = edgeChannel.IsEnabled = previousEdgeButton.IsEnabled = nextEdgeButton.IsEnabled = hasSignals;
            guide.IsVisible = hasSignals && Bounds.Width >= 1450;
            foreach (var choose in endpointPickButtons) choose.IsEnabled = hasSignals;
            waveTab.Foreground = Model.PacketsTab ? TimelineControl.Ink("#9BAEBF") : TimelineControl.Ink("#78D7C8"); packetTab.Foreground = Model.PacketsTab ? TimelineControl.Ink("#78D7C8") : TimelineControl.Ink("#9BAEBF");
            if (Model.PacketsTab)
            {
                var view = Model.PacketView(); var values = view.Items;
                if (!(packets.ItemsSource?.Cast<Packet>().SequenceEqual(values) ?? false)) packets.ItemsSource = values;
                packets.SelectedItem = values.Any(p => ReferenceEquals(p, Model.Selected)) ? Model.Selected : null;
                packetEmptyReason = view.EmptyReason; packetEmpty.IsVisible = view.EmptyReason != PacketEmptyReason.None; packets.IsVisible = !packetEmpty.IsVisible;
                packetEmptyAction.IsEnabled = view.EmptyReason != PacketEmptyReason.NoCapture || reloadButton.IsEnabled;
                search.IsVisible = view.EmptyReason is not (PacketEmptyReason.NoCapture or PacketEmptyReason.NoRecords);
                (packetEmptyText.Text, packetEmptyAction.Content) = view.EmptyReason switch
                {
                    PacketEmptyReason.NoCapture => ("No capture loaded.", "Open analysis"),
                    PacketEmptyReason.NoRecords => ("No decoded packets in this capture.", "View waveforms"),
                    PacketEmptyReason.Group => ("No packets in this Group.", "Show all packets"),
                    PacketEmptyReason.Search => ("No packets match this search.", "Clear search"),
                    _ => ("", "")
                };
            }
            if ((Model.A == null && Model.B == null) || (Model.Mode == 0 && Model.PinnedMeasurement != null && Model.Armed == null)) ResetNumericErrors();
            modePicker.SelectedIndex = Model.Mode == 2 ? 1 : 0; hint.Text = invalidTimes.Values.FirstOrDefault(value => value.Length > 0) ?? Model.Hint; hint.IsVisible = !string.IsNullOrEmpty(hint.Text);
            bool hasMarkers = Model.A != null || Model.B != null;
            measurement.IsVisible = !Model.PacketsTab && (hasMarkers || Model.Armed != null || hint.IsVisible);
            endpoints.IsVisible = Model.Mode != 0 && (hasMarkers || Model.Armed != null);
            modePicker.IsVisible = Model.Mode != 0;
            measurementActions.Children[1].IsVisible = Model.Armed != null;
            measurementActions.Children[2].IsVisible = Model.Armed == null && Model.A != null && Model.B != null;
            measurementActions.Children[3].IsVisible = hasMarkers || hint.IsVisible;
            if (!aTime.IsFocused && !invalidTimes.ContainsKey(aTime)) aTime.Text = Model.A == null ? "" : TimeAxis.Microseconds(Model.A.Sample, Model.Capture!.SampleRate);
            if (!bTime.IsFocused && !invalidTimes.ContainsKey(bTime)) bTime.Text = Model.B == null ? "" : TimeAxis.Microseconds(Model.B.Sample, Model.Capture!.SampleRate);
            aTime.IsEnabled = Model.A != null; bTime.IsEnabled = Model.B != null;
            UpdateMeasurementContext();
            delta.Text = Model.Armed is int armed ? $"Choose {(armed == 0 ? "A" : "B")} · {(Model.Mode == 2 ? "position" : "edge")}" : Model.PinnedMeasurement is { } local && Model.Capture is { } capture ? TimingText.Local(local, capture.SampleRate)
                : Model.A == null || Model.B == null ? "A/B measurement" : $"B − A   {(Model.B.Sample > Model.A.Sample ? "+" : "")}{TimingText.Duration((Model.B.Sample - Model.A.Sample) / (double)Model.Capture!.SampleRate)}";
            foreach (string unit in new[] { "ns", "µs", "ms", "s", "Hz", "kHz", "MHz" }) delta.Text = delta.Text?.Replace(" " + unit, "\u00a0" + unit, StringComparison.Ordinal);
            ToolTip.SetTip(delta, Model.Armed != null ? Model.Hint : Model.PinnedMeasurement is { } m ? $"Local measurement · pulse samples {m.Pulse?.Start}–{m.Pulse?.End}\n" + (m.Cycle is { } cycle ? $"Cycle samples {cycle.Start}–{cycle.End}. This is a local cycle, not a capture-wide frequency." : m.Reason) : "Signed interval between A and B");
            ToolTip.SetTip(aSource, Model.A == null ? "" : $"Sample {Model.A.Sample} · {(Model.A.IsEdge ? "edge" : "position")}"); ToolTip.SetTip(bSource, Model.B == null ? "" : $"Sample {Model.B.Sample} · {(Model.B.IsEdge ? "edge" : "position")}");
            bool showDetails = Model.Details && Model.Selected != null && !_decoder.IsOpen;
            bool showPanel = showDetails || _decoder.IsOpen;
            if (showPanel && workspace.ColumnDefinitions[2].Width.Value == 0) { workspace.ColumnDefinitions[1].Width = new(4); workspace.ColumnDefinitions[2].Width = new(320); }
            if (!showPanel) { workspace.ColumnDefinitions[1].Width = new(0); workspace.ColumnDefinitions[2].Width = new(0); }
            _decoderView.IsVisible = _decoder.IsOpen;
            inspectorBorder.IsVisible = showDetails; splitter.IsVisible = showPanel; detailButton.IsVisible = Model.Selected != null && !showDetails && !_decoder.IsOpen;
            if (showDetails && (!ReferenceEquals(renderedPacket, Model.Selected) || !ReferenceEquals(renderedIssue, Model.SelectedIssue) || renderedProtocolIndex != Model.SelectedProtocolIndex)) { RenderInspector(); renderedPacket = Model.Selected; renderedIssue = Model.SelectedIssue; renderedProtocolIndex = Model.SelectedProtocolIndex; }
            UpdateShellLayout();
            packetContext.Text = Model.PacketSelectionContext;
            packetContext.IsVisible = !string.IsNullOrEmpty(packetContext.Text);
        }
        finally { updating = false; }
    }
    private void QueueSignalReveal(IReadOnlyList<int> channels)
    {
        var capture = Model.Capture; var group = Model.Group; int request = ++revealGeneration;
        Dispatcher.UIThread.Post(() =>
        {
            if (request != revealGeneration || !ReferenceEquals(capture, Model.Capture) || !ReferenceEquals(group, Model.Group) || Model.PacketsTab) return;
            double offset = timeline.RevealOffset(channels, waveScroll.Offset.Y, waveScroll.Viewport.Height, waveScroll.Extent.Height);
            waveScroll.Offset = new Vector(waveScroll.Offset.X, offset);
            timeline.SetViewport(waveScroll.Offset.Y, waveScroll.Viewport.Height);
            UpdateMeasurementContext();
        }, DispatcherPriority.Loaded);
    }
    private string VerticalContext(int channel) => timeline.SourceVisibility(channel) switch
    {
        RowVisibility.Above => "Source above",
        RowVisibility.Below => "Source below",
        _ => ""
    };
    private void UpdateMeasurementContext()
    {
        if (endpointShowButtons.Count != 2) return;
        aSource.Text = EndpointLabel(Model.A, "A"); bSource.Text = EndpointLabel(Model.B, "B");
        foreach (var (point, button) in new[] { (Model.A, endpointShowButtons[0]), (Model.B, endpointShowButtons[1]) })
            button.IsVisible = point != null && (!Model.Group.Channels.Contains(point.Channel) || point.Sample < Model.Start || point.Sample > Model.End || VerticalContext(point.Channel).Length > 0);
        bool hasMarkers = Model.A != null || Model.B != null;
        string localSource = Model.PinnedMeasurement is { } pinned ? $"D{pinned.Channel} {Model.Capture?.Signals.FirstOrDefault(s => s.Id == pinned.Channel)?.Name}" : "A/B";
        string context = Model.PinnedMeasurement is { } pm ? !Model.Group.Channels.Contains(pm.Channel) ? "Outside group" : pm.Pulse is { } span && (span.End < Model.Start || span.Start > Model.End) ? "Outside time view" : VerticalContext(pm.Channel) : "";
        measurementTitle.Text = Model.Armed != null ? "Measure A/B" : !hasMarkers ? "Measurement" : context.Length > 0 ? $"Current · D{Model.PinnedMeasurement!.Channel}\n{context}" : "Current · " + localSource;
        ToolTip.SetTip(measurementTitle, Model.Armed != null ? Model.Hint : !hasMarkers ? "No current measurement" : "Current · " + localSource + (context.Length > 0 ? " · " + context : ""));
    }
    private async void RecoverPackets()
    {
        switch (packetEmptyReason)
        {
            case PacketEmptyReason.NoCapture:
                await OpenAnalysisAsync();
                break;
            case PacketEmptyReason.NoRecords: Model.SetTab(false); waveTab.Focus(); break;
            case PacketEmptyReason.Group: Model.ShowAllPackets(); search.Focus(); break;
            case PacketEmptyReason.Search: Model.Search(""); search.Focus(); break;
        }
    }
    private string EndpointLabel(Endpoint? endpoint, string name)
    {
        if (endpoint == null) return name;
        var edge = endpoint.IsEdge ? Model.Capture?.Signals.First(s => s.Id == endpoint.Channel).Nearest(endpoint.Sample) : null;
        string vertical = Model.Group.Channels.Contains(endpoint.Channel) ? VerticalContext(endpoint.Channel) : "";
        return $"{name} · D{endpoint.Channel} {(edge == null ? "position" : edge.High ? "↑" : "↓")}" + (Model.Capture?.HasSample(endpoint.Sample) == false ? "\nMissing samples" : "") + (Model.Group.Channels.Contains(endpoint.Channel) ? "" : "\nHidden signal") + (endpoint.Sample < Model.Start || endpoint.Sample > Model.End ? "\nOutside view" : "") + (vertical.Length > 0 ? "\n" + vertical : "");
    }
    private void CommitTime(int index, TextBox box)
    {
        if (updating || !box.IsEnabled) return;
        invalidTimes.TryGetValue(box, out var priorError);
        invalidTimes[box] = "";
        bool parsed = decimal.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value);
        if (!parsed || !Model.EditEndpointTime(index, value))
        {
            box.BorderBrush = TimelineControl.Ink("#FF9AAA");
            invalidTimes[box] = parsed ? Model.Hint : "Enter a numeric time in µs. Esc restores the current endpoint.";
            Model.MeasurementError(invalidTimes[box]);
            return;
        }
        invalidTimes.Remove(box); box.ClearValue(BorderBrushProperty);
        if (!string.IsNullOrEmpty(priorError)) Model.ClearMeasurementError(priorError);
        var point = index == 0 ? Model.A : Model.B; box.Text = point == null ? "" : TimeAxis.Microseconds(point.Sample, Model.Capture!.SampleRate);
        Refresh();
    }
    private void ResetNumericErrors() { invalidTimes.Clear(); aTime.ClearValue(BorderBrushProperty); bTime.ClearValue(BorderBrushProperty); }
    private void CancelEditing()
    {
        ResetNumericErrors();
        Model.CancelPick();
        aTime.Text = Model.A == null ? "" : TimeAxis.Microseconds(Model.A.Sample, Model.Capture!.SampleRate);
        bTime.Text = Model.B == null ? "" : TimeAxis.Microseconds(Model.B.Sample, Model.Capture!.SampleRate);
    }
    private void RenderInspector()
    {
        inspector.Children.Clear(); var packet = Model.Selected!;
        if (!ReferenceEquals(renderedPacket, packet)) bytePage = 0;
        var heading = new Grid { ColumnDefinitions = new("*,Auto") }; Add(heading, Text(packet.Decoder.ToUpperInvariant() + " / " + packet.Id, true), 0, 0); Add(heading, Button("×", Model.ToggleDetails, "flat", "Close packet details"), 0, 1); inspector.Children.Add(heading);
        var title = Text(packet.Title); title.Bind(TextBlock.FontSizeProperty, new DynamicResourceExtension("HeadingFontSize")); title.FontWeight = FontWeight.SemiBold; title.TextWrapping = TextWrapping.Wrap; inspector.Children.Add(title);
        inspector.Children.Add(Text(TimeAxis.FormatRange(packet.Span.Start, packet.Span.End, Model.Capture!.SampleRate), true));
        packetContext.TextWrapping = TextWrapping.Wrap; inspector.Children.Add(packetContext);
        inspector.Children.Add(Text(packet.IsSerial ? packet.DataSummary : packet.Address + (packet.I2c is { AddressObserved: true } wire ? wire.Read ? " · Read" : " · Write" : "") + " · " + packet.DataSummary));
        RenderProtocolDetails(packet);
        if (packet.Fields.Count > 0)
        {
            inspector.Children.Add(new Separator()); inspector.Children.Add(Text("DemoFrame fields", true));
            foreach (var field in packet.Fields)
            {
                var row = new Grid { ColumnDefinitions = new("*,Auto") }; Add(row, Text(field.Name), 0, 0);
                var value = field.Name == "Payload" && packet.Value is int number ? $"0x{number:X4}" : "0x" + string.Join("", field.Raw.Select(b => b.ToString("X2")));
                var fieldValue = Text(value); fieldValue.FontFamily = new("Cascadia Mono,Consolas"); fieldValue.Bind(TextBlock.FontSizeProperty, new DynamicResourceExtension("DataFontSize")); Add(row, fieldValue, 0, 1); inspector.Children.Add(row);
                if (Model.SelectedProtocol is { Kind: "data" } part && field.Spans.Any(s => s.Start < part.Span.End && s.End > part.Span.Start))
                { row.Background = TimelineControl.Ink("#173C39"); ToolTip.SetTip(row, "Field source overlaps the selected decoded interval"); }
                if (Model.Issues.Any(i => ReferenceEquals(i.Packet, packet) && i.Diagnostic.Field == field.Name))
                {
                    ((TextBlock)row.Children[0]).Text += " !"; ((TextBlock)row.Children[0]).Foreground = fieldValue.Foreground = TimelineControl.Ink("#FF9AAA");
                }
            }
        }
        foreach (var issue in Model.Issues.Where(i => ReferenceEquals(i.Packet, packet)))
        {
            var diagnostic = new StackPanel { Spacing = 6 };
            diagnostic.Children.Add(new TextBlock { Text = issue.Diagnostic.Layer + " · " + issue.Diagnostic.Message, TextWrapping = TextWrapping.Wrap, Foreground = TimelineControl.Ink("#FF9AAA") });
            diagnostic.Children.Add(Text(issue.Diagnostic.Field is { } field ? $"Evidence · {field}" : "Evidence · annotated interval", true));
            diagnostic.Children.Add(Button("Locate issue", () => Model.LocateIssue(issue), "flat", "Locate the diagnostic evidence, preserving timing markers"));
            inspector.Children.Add(new Border { Child = diagnostic, Padding = new(12), Background = TimelineControl.Ink("#40232D"), BorderBrush = TimelineControl.Ink("#FF9AAA"), BorderThickness = new(ReferenceEquals(Model.SelectedIssue, issue) ? 1 : 0) });
        }
        if (packet.I2c != null) { var busText = Text(TimingText.I2cStatus(packet), true); busText.TextWrapping = TextWrapping.Wrap; inspector.Children.Add(busText); }
        if (Model.SelectedProtocol == null) inspector.Children.Add(Button("Locate source", Model.Locate));
    }
    private void ShowActivity()
    {
        if (Model.Capture is not { } capture) return;
        var body = new StackPanel { Spacing = 10, Width = 570 };
        body.Children.Add(Text("Signal activity · all captured channels"));
        body.Children.Add(Text($"Full capture compared with {Model.RangeText}", true));
        var rows = new StackPanel { Spacing = 5 };
        var heading = new Grid { ColumnDefinitions = new("185,150,150,Auto") };
        Add(heading, Text("Signal", true), 0, 0); Add(heading, Text("Full capture", true), 0, 1); Add(heading, Text("Current view", true), 0, 2); body.Children.Add(heading);
        foreach (var signal in capture.Signals)
        {
            var row = new Grid { ColumnDefinitions = new("185,150,150,Auto") };
            var name = Text($"D{signal.Id}  {signal.Name}"); name.TextWrapping = TextWrapping.Wrap; name.Margin = new(0, 0, 8, 0); Add(row, name, 0, 0);
            var full = Text(ActivityText.Full(capture.Activity(signal.Id, 0, capture.SampleCount)), true); full.TextWrapping = TextWrapping.Wrap; full.Margin = new(0, 0, 8, 0); Add(row, full, 0, 1);
            var view = Text(ActivityText.Full(capture.Activity(signal.Id, (long)Math.Ceiling(Model.Start), (long)Math.Ceiling(Model.End))), true); view.TextWrapping = TextWrapping.Wrap; view.Margin = new(0, 0, 8, 0); Add(row, view, 0, 2);
            var show = Button("Show", () => Model.ShowSignal(signal.Id), "flat", "Reveal this channel without changing time"); AutomationProperties.SetName(show, $"Show D{signal.Id} {signal.Name}"); Add(row, show, 0, 3); rows.Children.Add(row);
        }
        body.Children.Add(new ScrollViewer { Content = rows, MaxHeight = 360, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        openPanel = new Flyout { Content = body }; openPanel.FlyoutPresenterClasses.Add("analysis"); openPanel.ShowAt(_shell.Activity);
    }
    private void ShowIssues()
    {
        var body = new StackPanel { Spacing = 14, Width = 420 };
        body.Children.Add(Text($"Capture issues · {Model.Issues.Count}")); body.Children.Add(Text("Entire capture · all decoder layers", true));
        var rows = new StackPanel { Spacing = 12 };
        foreach (var issue in Model.Issues)
        {
            var row = new StackPanel { Spacing = 5 };
            var label = Text(issue.Diagnostic.Layer + " · " + issue.Diagnostic.Message); label.TextWrapping = TextWrapping.Wrap; row.Children.Add(label);
            var span = issue.Diagnostic.Spans.FirstOrDefault() ?? issue.Packet.Span;
            row.Children.Add(Text($"{issue.Packet.Id} · {TimeAxis.FormatRange(span.Start, span.End, Model.Capture!.SampleRate)} · " + (issue.Diagnostic.Field ?? "annotated interval"), true));
            var locate = Button("Locate issue", () => Model.LocateIssue(issue), "flat"); AutomationProperties.SetName(locate, $"Locate issue {issue.Diagnostic.Layer} {issue.Diagnostic.Message}"); row.Children.Add(locate); rows.Children.Add(row);
        }
        body.Children.Add(new ScrollViewer { Content = rows, MaxHeight = 360 }); openPanel = new Flyout { Content = body }; openPanel.FlyoutPresenterClasses.Add("analysis"); openPanel.ShowAt(_shell.Issues);
    }
    private async void EditGroup(bool create)
    {
        var original = Model.Group;
        bool isNew = create || Model.IsAllSignals;
        var draft = new GroupEditState(Model.Capture, create ? null : original);
        var dialog = new Window { Title = isNew ? "New group" : "Edit observation group", Width = 640, Height = 600, MinWidth = 520, MinHeight = 380, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var body = new StackPanel { Spacing = 14, Margin = new(20) };
        var name = new TextBox { Text = isNew ? "New group" : original.Name, PlaceholderText = "Group name", MaxLength = 40 };
        AutomationProperties.SetName(name, "Group name");
        body.Children.Add(Text("Group name", true)); body.Children.Add(name);
        body.Children.Add(new TextBlock { Text = "Choose what to view together. Capture settings stay unchanged.", TextWrapping = TextWrapping.Wrap, Foreground = TimelineControl.Ink("#9BAEBF") });
        body.Children.Add(Text("Decoder tracks · includes required signals", true));
        var checks = new Dictionary<int, CheckBox>(); var decoderChecks = new Dictionary<string, CheckBox>(); bool changing = false;
        void RefreshChecks()
        {
            changing = true;
            foreach (var (id, check) in checks) check.IsChecked = draft.Channels.Contains(id);
            foreach (var (id, check) in decoderChecks) check.IsChecked = draft.Decoders.Contains(id);
            changing = false;
        }
        var decoderRow = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var decoder in draft.OfferedDecoders)
        {
            string label = decoder.Name + (draft.HasSources(decoder) ? "" : " · source not captured");
            var check = new CheckBox { Content = label, IsChecked = draft.Decoders.Contains(decoder.Id), Margin = new(0, 0, 18, 4) }; decoderChecks[decoder.Id] = check;
            check.IsCheckedChanged += (_, _) => { if (changing) return; draft.SetDecoder(decoder.Id, check.IsChecked == true); RefreshChecks(); }; decoderRow.Children.Add(check);
        }
        body.Children.Add(decoderRow);
        if (draft.OfferedDecoders.Count == 0) body.Children.Add(Text("No decoder tracks have their source signals in this capture.", true));
        void AddSignals(IEnumerable<int> members, bool missing)
        {
            var ids = members.Order().ToArray();
            if (ids.Length == 0) return;
            body.Children.Add(Text(missing ? "Saved signals · not in this capture" : $"Captured signals · {ids.Length} · probe order", true));
            if (missing) body.Children.Add(Text("Kept for other captures. Uncheck to remove.", true));
            var channelGrid = new UniformGrid { Columns = ids.Length <= 8 ? 2 : 3 };
            foreach (int id in ids)
            {
                var signal = Model.Capture?.Signals.FirstOrDefault(s => s.Id == id);
                string label = $"D{id}" + (signal == null ? "" : "  " + signal.Name);
                var check = new CheckBox { Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }, IsChecked = draft.Channels.Contains(id), Margin = new(0, 0, 10, 6), HorizontalContentAlignment = HorizontalAlignment.Stretch };
                AutomationProperties.SetName(check, label + (missing ? " · Not captured" : "")); checks[id] = check;
                check.IsCheckedChanged += (_, _) => { if (changing) return; draft.SetChannel(id, check.IsChecked == true); RefreshChecks(); }; channelGrid.Children.Add(check);
            }
            body.Children.Add(channelGrid);
        }
        AddSignals(draft.CapturedChannels, false);
        AddSignals(draft.OfferedChannels.Except(draft.CapturedChannels), true);
        var footer = new StackPanel { Spacing = 10, Margin = new(20, 12, 20, 20) };
        var error = Text(""); error.Foreground = TimelineControl.Ink("#FF9AAA"); error.TextWrapping = TextWrapping.Wrap; error.IsVisible = false; footer.Children.Add(error);
        var actions = Row();
        if (!isNew) actions.Children.Add(Button("New group", () => { dialog.Close(); EditGroup(true); }));
        else if (!create) actions.Children.Add(Button("Start empty", () => { foreach (int id in draft.Channels.ToArray()) draft.SetChannel(id, false); RefreshChecks(); }));
        var cancel = Button("Cancel", dialog.Close); cancel.IsCancel = true; actions.Children.Add(cancel);
        var save = Button(isNew ? "Create group" : "Save group", () =>
        {
            if (!draft.TryBuild(name.Text, Model.Groups, isNew ? null : original, out var edited, out var message)) { error.Text = message; error.IsVisible = true; return; }
            if (isNew) Model.Groups.Insert(Model.Groups.Count - 1, edited!); else Model.Groups[Model.Groups.IndexOf(original)] = edited!;
            Model.SetGroup(edited!); SavePreferences(); dialog.Close();
        }, "primary"); actions.Children.Add(save); footer.Children.Add(actions);
        var layout = new Grid { RowDefinitions = new("*,Auto") }; Add(layout, new ScrollViewer { Content = body, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }, 0); Add(layout, Band(footer), 1);
        dialog.Content = layout; dialog.Opened += (_, _) => name.Focus(); await dialog.ShowDialog(this);
    }
    private void EditColor(int channel, Control target)
    {
        if (Model.Capture?.Signals.FirstOrDefault(s => s.Id == channel) is not { } signal) return;
        openPanel?.Hide();
        var editor = new ChannelAppearanceViewModel(Model, signal);
        var panel = new Flyout { Content = new ChannelAppearanceView { DataContext = editor } };
        panel.FlyoutPresenterClasses.Add("analysis");
        editor.Applied += SavePreferences; editor.DismissRequested += panel.Hide;
        panel.Closed += (_, _) => editor.Cancel();
        openPanel = panel; panel.ShowAt(target);
    }
    private async void ShowInfo()
    {
        if (fileBusy) return;
        if (Model.Capture is not { } capture) return;
        var dialog = new Window { Title = "Capture source", Width = 540, Height = 530, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var body = new StackPanel { Margin = new(22), Spacing = 14 };
        body.Children.Add(new TextBlock { Text = "Current capture · " + (analysisPath == null ? CurrentSourceName : Path.GetFileName(analysisPath)), TextWrapping = TextWrapping.Wrap });
        body.Children.Add(Text($"{capture.Signals.Count} channels · {capture.SampleRate / 1e6:0.##} MS/s · {TimingText.Duration(capture.SampleCount / (double)capture.SampleRate)}"));
        if (analysisPath != null) body.Children.Add(new TextBlock { Text = "Saved analysis · " + sessionSourceName + ". Decoded results are saved snapshots; opening does not run a decoder.", TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text = capture.SourceDescription, TextWrapping = TextWrapping.Wrap, Foreground = TimelineControl.Ink("#9BAEBF") });
        if (capture.Gaps.Count == 0) body.Children.Add(Text("Coverage · no missing intervals", true));
        foreach (var gap in capture.Gaps)
        {
            var row = Row(); row.Children.Add(Text($"Missing samples · {TimingText.Duration(gap.Start / (double)capture.SampleRate)}–{TimingText.Duration(gap.End / (double)capture.SampleRate)}", true));
            row.Children.Add(Button("Show interval", () => { dialog.Close(); Model.Remember(); Model.SetTab(false); double pad = gap.End - gap.Start; Model.SetRange(gap.Start - pad, gap.End + pad); }, "flat", "Locate this missing-data interval while preserving measurements")); body.Children.Add(row);
        }
        body.Children.Add(new Separator()); body.Children.Add(Text("Offline example"));
        var picker = new ComboBox { ItemsSource = DemoCaptureSource.Examples, SelectedItem = activeExample, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(picker, "Offline example"); body.Children.Add(picker);
        var description = new TextBlock { Text = activeExample.Description, TextWrapping = TextWrapping.Wrap }; body.Children.Add(description);
        picker.SelectionChanged += (_, _) => { if (picker.SelectedItem is DemoExample selected) description.Text = selected.Description; };
        body.Children.Add(new TextBlock { Text = "Opening an example clears measurements, selection and search. " + (personalPreferences == null ? "Your Groups and waveform colors are kept." : "Your default Groups and waveform colors will return."), TextWrapping = TextWrapping.Wrap, Foreground = TimelineControl.Ink("#9BAEBF") });
        var actions = Row(); actions.Children.Add(Button("Close", dialog.Close)); actions.Children.Add(Button("Open example", async () => { var selected = picker.SelectedItem as DemoExample ?? activeExample; dialog.Close(); await LoadAsync(selected); }, "primary")); body.Children.Add(actions);
        dialog.Content = new ScrollViewer { Content = body }; await dialog.ShowDialog(this);
    }
    private void RestorePreferences()
    {
        try
        {
            if (!File.Exists(preferencePath)) return;
            var preferences = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(preferencePath)); if (preferences?.Version != 1) return;
            var valid = GroupEditState.Restore(preferences.Groups);
            if (valid.Length > 0) { Model.Groups.Clear(); foreach (var group in valid) Model.Groups.Add(group); Model.Groups.Add(new("All signals", Enumerable.Range(0, 32).ToArray(), [])); }
            Model.SetGroup(Model.Groups.FirstOrDefault(g => g.Name == preferences.ActiveGroup) ?? Model.Groups[0]);
            foreach (var pair in preferences.Colors ?? []) if (pair.Key is >= 0 and < 32 && Color.TryParse(pair.Value, out _)) Model.Colors[pair.Key] = pair.Value;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { /* Preserve a malformed preference file; start with defaults. */ }
    }
    private void SavePreferences()
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(preferencePath)!); var temp = preferencePath + ".tmp"; File.WriteAllText(temp, JsonSerializer.Serialize(personalPreferences ?? CurrentPreferences(), new JsonSerializerOptions { WriteIndented = true })); File.Move(temp, preferencePath, true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { status.Text = "Settings could not be saved: " + ex.Message; }
    }
    private sealed record Preferences(int Version, string ActiveGroup, ObservationGroup[] Groups, Dictionary<int, string> Colors);
    private static TextBlock Text(string value, bool muted = false)
    {
        var text = new TextBlock { Text = value, VerticalAlignment = VerticalAlignment.Center, Foreground = TimelineControl.Ink(muted ? "#9BAEBF" : "#E4EAF0") };
        text.Bind(TextBlock.FontSizeProperty, new DynamicResourceExtension(muted ? "CaptionFontSize" : "BodyFontSize")); return text;
    }
    private static StackPanel Row() => new() { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
    private static Button Button(string label, Action action, string? style = null, string? tip = null) { var button = new Button { Content = label, VerticalAlignment = VerticalAlignment.Center }; if (style != null) button.Classes.Add(style); if (tip != null) { ToolTip.SetTip(button, tip); if (string.IsNullOrWhiteSpace(label) || label is "←" or "−" or "+" or "‹" or "›" or "×") AutomationProperties.SetName(button, tip); } button.Click += (_, _) => action(); return button; }
    private static Border Band(Control child) => new() { BorderThickness = new(0, 0, 0, 1), BorderBrush = TimelineControl.Ink("#26343F"), Child = child };
    private static void Add(Grid grid, Control child, int row, int column = 0) { Grid.SetRow(child, row); Grid.SetColumn(child, column); grid.Children.Add(child); }
}
