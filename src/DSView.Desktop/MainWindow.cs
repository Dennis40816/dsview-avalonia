// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using DSView.Domain;
using DSView.Infrastructure;

namespace DSView.Desktop;

public sealed class MainWindow : Window
{
    public Workbench Model { get; } = new();
    private readonly ComboBox groupPicker = new() { MinWidth = 205 };
    private readonly ComboBox modePicker = new() { ItemsSource = new[] { "Nearby edges", "A/B edge", "A/B position" }, SelectedIndex = 0, Width = 155 };
    private readonly ComboBox edgeChannel = new() { Width = 74 };
    private readonly TextBlock range = Text(""), groupSummary = Text("", true), status = Text("Loading saved demo…", true), hint = Text("", true), delta = Text(""), footer = Text("", true), selectedLabel = Text("", true);
    private readonly TextBox aTime = new() { Width = 95 }, bTime = new() { Width = 95 };
    private readonly TextBlock aSource = Text("A", true), bSource = Text("B", true);
    private readonly StackPanel endpoints = Row(), inspector = new() { Spacing = 14, Margin = new Thickness(18) };
    private readonly Grid workspace = new() { ColumnDefinitions = new("*,0,0") };
    private readonly ListBox packets = new() { FontFamily = new("Cascadia Mono,Consolas"), FontSize = 12, Background = Brushes.Transparent };
    private readonly TextBox search = new() { PlaceholderText = "Search packets, Hex or fields", Margin = new(14, 8) };
    private readonly Button waveTab, packetTab, detailButton, reloadButton;
    private readonly Control wavePanel, packetPanel;
    private readonly Border inspectorBorder;
    private readonly GridSplitter splitter = new() { Width = 4, Background = TimelineControl.Ink("#26343F"), ResizeDirection = GridResizeDirection.Columns };
    private CancellationTokenSource? loadCancellation;
    private int generation;
    private bool updating;
    private readonly string preferencePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DSView2", "workbench.json");
    public MainWindow()
    {
        Title = "DSView2 · Native workbench · Offline demo"; Width = 1380; Height = 850; MinWidth = 1024; MinHeight = 650;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        RestorePreferences();
        var page = new Grid { RowDefinitions = new("50,44,42,48,*,38,Auto,28") };
        var header = new Grid { ColumnDefinitions = new("Auto,Auto,Auto,*,Auto,Auto"), Margin = new(16, 0) };
        var brand = Text("DSView2"); brand.FontWeight = FontWeight.SemiBold; brand.Margin = new(0, 0, 20, 0);
        Add(header, brand, 0, 0);
        waveTab = Button("Waveforms", () => Model.SetTab(false), "flat"); packetTab = Button("Packets", () => Model.SetTab(true), "flat");
        Add(header, waveTab, 0, 1); Add(header, packetTab, 0, 2); status.HorizontalAlignment = HorizontalAlignment.Right; status.Margin = new(12, 0); Add(header, status, 0, 3);
        Add(header, Button("Demo info", ShowInfo), 0, 4); reloadButton = Button("Reload demo", async () => await LoadAsync(), "primary"); reloadButton.Margin = new(10, 0, 0, 0); Add(header, reloadButton, 0, 5);
        Add(page, Band(header), 0);
        var groupBar = new Grid { ColumnDefinitions = new("Auto,Auto,Auto,*,Auto"), Margin = new(16, 0) };
        Add(groupBar, Text("Group", true), 0, 0); groupPicker.Margin = new(10, 0); groupPicker.ItemsSource = Model.Groups; groupPicker.SelectedItem = Model.Group;
        groupPicker.SelectionChanged += (_, _) => { if (!updating && groupPicker.SelectedItem is ObservationGroup group) Model.SetGroup(group); };
        Add(groupBar, groupPicker, 0, 1); Add(groupBar, Button("Edit group", () => EditGroup(false), "flat"), 0, 2); groupSummary.Margin = new(12, 0); Add(groupBar, groupSummary, 0, 3); Add(groupBar, Button("All signals · 32", Model.ShowAll), 0, 4); Add(page, Band(groupBar), 1);
        var nav = new Grid { ColumnDefinitions = new("Auto,Auto,Auto,Auto,*,Auto,Auto,Auto,Auto"), Margin = new(16, 0) };
        Add(nav, Button("←", Model.Previous, "flat", "Previous view"), 0, 0); Add(nav, Button("Fit time", Model.FitTime, "flat"), 0, 1);
        Add(nav, Button("−", () => Model.Zoom(1.5), "flat", "Zoom out"), 0, 2); Add(nav, Button("+", () => Model.Zoom(1 / 1.5), "flat", "Zoom in"), 0, 3);
        range.Margin = new(14, 0); Add(nav, range, 0, 4); Add(nav, Text("Edge", true), 0, 5); edgeChannel.Margin = new(8, 0); Add(nav, edgeChannel, 0, 6);
        Add(nav, Button("‹", () => Model.StepEdge(SelectedEdgeChannel(), -1), "flat", "Previous edge"), 0, 7); Add(nav, Button("›", () => Model.StepEdge(SelectedEdgeChannel(), 1), "flat", "Next edge"), 0, 8); Add(page, nav, 2);
        Add(page, new TimelineControl(Model, true), 3);
        var timeline = new TimelineControl(Model); timeline.ColorRequested += EditColor;
        wavePanel = new ScrollViewer { Content = timeline, VerticalContentAlignment = VerticalAlignment.Top, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var packetGrid = new Grid { RowDefinitions = new("Auto,*") }; Add(packetGrid, search, 0); Add(packetGrid, packets, 1); packetPanel = packetGrid;
        search.TextChanged += (_, _) => { if (!updating) Model.Search(search.Text ?? ""); };
        packets.SelectionChanged += (_, _) => { if (!updating && packets.SelectedItem is Packet packet) Model.Select(packet); };
        var work = new Grid(); work.Children.Add(wavePanel); work.Children.Add(packetPanel); Add(workspace, work, 0, 0); Add(workspace, splitter, 0, 1);
        inspectorBorder = new Border { BorderBrush = TimelineControl.Ink("#26343F"), BorderThickness = new(1, 0, 0, 0), Child = new ScrollViewer { Content = inspector, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } };
        Add(workspace, inspectorBorder, 0, 2); Add(page, workspace, 4);
        var selection = new Grid { ColumnDefinitions = new("*,Auto,Auto"), Margin = new(16, 0) }; Add(selection, selectedLabel, 0, 0); Add(selection, Button("Locate source", Model.Locate, "flat"), 0, 1); detailButton = Button("Details", Model.ToggleDetails, "flat"); Add(selection, detailButton, 0, 2); Add(page, Band(selection), 5);
        var measurement = new StackPanel { Spacing = 8, Margin = new(16, 8) };
        var modes = new Grid { ColumnDefinitions = new("Auto,Auto,Auto,Auto,*") }; Add(modes, Text("Measure", true), 0, 0); modePicker.Margin = new(10, 0); Add(modes, modePicker, 0, 1);
        Add(modes, Button("Choose A", () => Model.Arm(0), "flat"), 0, 2); Add(modes, Button("Choose B", () => Model.Arm(1), "flat"), 0, 3); hint.TextTrimming = TextTrimming.CharacterEllipsis; hint.Margin = new(12, 0); Add(modes, hint, 0, 4);
        modePicker.SelectionChanged += (_, _) => { if (!updating) Model.SetMode(modePicker.SelectedIndex); };
        endpoints.Children.Add(aSource); endpoints.Children.Add(aTime); endpoints.Children.Add(Text("µs", true)); endpoints.Children.Add(bSource); endpoints.Children.Add(bTime); endpoints.Children.Add(Text("µs", true)); delta.Foreground = TimelineControl.Ink("#78D7C8"); delta.Margin = new(20, 0); endpoints.Children.Add(delta);
        endpoints.Children.Add(Button("Fit A/B", Model.FitMarkers, "flat")); endpoints.Children.Add(Button("Clear", Model.Clear, "flat"));
        aTime.KeyDown += (_, e) => { if (e.Key == Key.Enter) CommitTime(0, aTime); }; bTime.KeyDown += (_, e) => { if (e.Key == Key.Enter) CommitTime(1, bTime); };
        aTime.LostFocus += (_, _) => CommitTime(0, aTime); bTime.LostFocus += (_, _) => CommitTime(1, bTime);
        measurement.Children.Add(modes); measurement.Children.Add(endpoints); Add(page, Band(measurement), 6);
        var bottom = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new(16, 0) }; Add(bottom, footer, 0, 0); Add(bottom, Text("50 MS/s · Synthetic source · Native Avalonia", true), 0, 1); Add(page, Band(bottom), 7);
        Content = page; Model.Changed += Refresh;
        Opened += async (_, _) => await LoadAsync(); Closing += (_, _) => { loadCancellation?.Cancel(); SavePreferences(); };
        KeyDown += (_, e) => { if (e.Key == Key.Escape && Model.Details) Model.ToggleDetails(); };
        Refresh();
    }
    private int SelectedEdgeChannel() => edgeChannel.SelectedItem is string str && int.TryParse(str.AsSpan(1), out var id) ? id : Model.Group.Channels.FirstOrDefault();
    private async Task LoadAsync()
    {
        loadCancellation?.Cancel(); loadCancellation?.Dispose(); loadCancellation = new(); int current = ++generation;
        status.Text = "Loading saved demo…"; reloadButton.IsEnabled = false;
        try { var capture = await DemoCaptureSource.LoadAsync(loadCancellation.Token); if (current == generation) { Model.Load(capture); status.Text = "Saved demo · 1 ms · offline"; } }
        catch (OperationCanceledException) { }
        catch (Exception ex) { status.Text = "Could not load demo: " + ex.Message; }
        finally { if (current == generation) reloadButton.IsEnabled = true; }
    }
    private void Refresh()
    {
        if (updating) return; updating = true;
        try
        {
            groupPicker.SelectedItem = Model.Group; groupSummary.Text = $"{Model.Group.Channels.Length} signals · {Model.Group.Decoders.Length} decoders";
            range.Text = Model.RangeText; footer.Text = $"32 physical · 32 captured · {Model.Group.Channels.Length} in view";
            var old = edgeChannel.SelectedItem as string; var choices = Model.Group.Channels.Select(c => $"D{c}").ToArray(); edgeChannel.ItemsSource = choices; edgeChannel.SelectedItem = choices.Contains(old) ? old : choices.FirstOrDefault();
            wavePanel.IsVisible = !Model.PacketsTab; packetPanel.IsVisible = Model.PacketsTab;
            waveTab.Foreground = Model.PacketsTab ? TimelineControl.Ink("#9BAEBF") : TimelineControl.Ink("#78D7C8"); packetTab.Foreground = Model.PacketsTab ? TimelineControl.Ink("#78D7C8") : TimelineControl.Ink("#9BAEBF");
            if (Model.PacketsTab) { packets.ItemsSource = Model.Packets.ToArray(); packets.SelectedItem = Model.Selected; }
            modePicker.SelectedIndex = Model.Mode; hint.Text = Model.Hint; ToolTip.SetTip(hint, Model.Hint);
            endpoints.IsVisible = Model.A != null || Model.B != null;
            if (!aTime.IsFocused) aTime.Text = Model.A == null ? "" : Model.Us(Model.A.Sample).ToString("0.###", CultureInfo.InvariantCulture);
            if (!bTime.IsFocused) bTime.Text = Model.B == null ? "" : Model.Us(Model.B.Sample).ToString("0.###", CultureInfo.InvariantCulture);
            aTime.IsEnabled = Model.A != null; bTime.IsEnabled = Model.B != null;
            aSource.Text = EndpointLabel(Model.A, "A"); bSource.Text = EndpointLabel(Model.B, "B");
            delta.Text = Model.A == null || Model.B == null ? "Choose both endpoints" : $"Δt {Model.Us(Model.B.Sample - Model.A.Sample):0.###} µs";
            ToolTip.SetTip(aSource, Model.A == null ? "" : $"Sample {Model.A.Sample} · {(Model.A.IsEdge ? "edge" : "position")}"); ToolTip.SetTip(bSource, Model.B == null ? "" : $"Sample {Model.B.Sample} · {(Model.B.IsEdge ? "edge" : "position")}");
            selectedLabel.Text = Model.Selected == null ? "Select a protocol item or packet to inspect it." : $"{Model.Selected.Id} · {Model.Selected.Address}" + (!Model.SelectionInGroup ? " · Packet outside group" : Model.Selected.Span.End < Model.Start || Model.Selected.Span.Start > Model.End ? " · Outside time view" : "");
            bool showDetails = Model.Details && Model.Selected != null;
            if (showDetails && workspace.ColumnDefinitions[2].Width.Value == 0) { workspace.ColumnDefinitions[1].Width = new(4); workspace.ColumnDefinitions[2].Width = new(320); }
            if (!showDetails) { workspace.ColumnDefinitions[1].Width = new(0); workspace.ColumnDefinitions[2].Width = new(0); }
            inspectorBorder.IsVisible = splitter.IsVisible = showDetails; detailButton.IsEnabled = Model.Selected != null; detailButton.Content = showDetails ? "Hide details" : "Details";
            if (showDetails) RenderInspector();
        }
        finally { updating = false; }
    }
    private string EndpointLabel(Endpoint? endpoint, string name) => endpoint == null ? name : $"{name} · D{endpoint.Channel}" + (Model.Group.Channels.Contains(endpoint.Channel) ? "" : " (hidden)");
    private void CommitTime(int index, TextBox box)
    {
        if (updating || !box.IsEnabled) return;
        if (!double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !Model.EditEndpoint(index, value))
        { var point = index == 0 ? Model.A : Model.B; box.Text = point == null ? "" : Model.Us(point.Sample).ToString("0.###", CultureInfo.InvariantCulture); }
    }
    private void RenderInspector()
    {
        inspector.Children.Clear(); var packet = Model.Selected!;
        inspector.Children.Add(Text(packet.Decoder.ToUpperInvariant() + "  /  " + packet.Id, true));
        var title = Text(packet.Title); title.FontSize = 18; title.FontWeight = FontWeight.SemiBold; inspector.Children.Add(title);
        inspector.Children.Add(Text($"{Model.Us(packet.Span.Start):0.##}–{Model.Us(packet.Span.End):0.##} µs", true));
        inspector.Children.Add(Text(packet.Address + " · " + packet.Bytes.Length + " data bytes"));
        inspector.Children.Add(Text("Data bytes · Hex", true));
        inspector.Children.Add(new TextBlock { Text = packet.Hex, FontFamily = new("Cascadia Mono,Consolas"), FontSize = 15, TextWrapping = TextWrapping.Wrap });
        if (packet.Fields.Count > 0)
        {
            inspector.Children.Add(new Separator()); inspector.Children.Add(Text("DemoFrame fields", true));
            foreach (var field in packet.Fields)
            {
                var row = new Grid { ColumnDefinitions = new("*,Auto") }; Add(row, Text(field.Name), 0, 0);
                var value = field.Name == "Payload" && packet.Value is int number ? $"0x{number:X4}" : "0x" + string.Join("", field.Raw.Select(b => b.ToString("X2")));
                Add(row, Text(value), 0, 1); inspector.Children.Add(row);
            }
        }
        if (packet.Error != null)
        {
            var error = new TextBlock { Text = (packet.Decoder == "i2c" ? "DemoFrame · " : "") + packet.Error, TextWrapping = TextWrapping.Wrap, Foreground = TimelineControl.Ink("#FF9AAA") };
            inspector.Children.Add(new Border { Child = error, Padding = new(12), Background = TimelineControl.Ink("#40232D") });
        }
        if (packet.Decoder == "i2c") inspector.Children.Add(new TextBlock { Text = "I²C · Address and all 6 data bytes ACK. DemoFrame diagnostics do not invalidate the bus transaction.", TextWrapping = TextWrapping.Wrap, Foreground = TimelineControl.Ink("#9BAEBF"), FontSize = 12 });
        inspector.Children.Add(Button("Locate source", Model.Locate));
    }
    private async void EditGroup(bool create)
    {
        var original = Model.Group;
        var dialog = new Window { Title = create ? "New group" : "Edit observation group", Width = 590, Height = 600, MinWidth = 500, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var body = new StackPanel { Spacing = 14, Margin = new(20) };
        var name = new TextBox { Text = create || original.Name == "All signals" ? "New group" : original.Name, PlaceholderText = "Group name", MaxLength = 40 };
        body.Children.Add(Text("Group name", true)); body.Children.Add(name); body.Children.Add(Text("Decoders · required source signals are included", true));
        var channelSet = new HashSet<int>(create ? [] : original.Channels); var decoderSet = new HashSet<string>(create ? [] : original.Decoders);
        var checks = new Dictionary<int, CheckBox>(); var decoderChecks = new Dictionary<string, CheckBox>(); bool changing = false;
        var decoderRow = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var decoder in Capture.Decoders)
        {
            var check = new CheckBox { Content = decoder.Name, IsChecked = decoderSet.Contains(decoder.Id), Margin = new(0, 0, 18, 0) }; decoderChecks[decoder.Id] = check;
            check.IsCheckedChanged += (_, _) => { if (changing) return; changing = true; if (check.IsChecked == true) { decoderSet.Add(decoder.Id); foreach (var id in decoder.Channels) { channelSet.Add(id); if (checks.TryGetValue(id, out var source)) source.IsChecked = true; } } else decoderSet.Remove(decoder.Id); changing = false; }; decoderRow.Children.Add(check);
        }
        body.Children.Add(decoderRow); body.Children.Add(Text("Signals · physical probe order", true));
        var channelGrid = new UniformGrid { Columns = 4 };
        for (int i = 0; i < 32; i++)
        {
            int id = i; var signal = Model.Capture?.Signals.FirstOrDefault(s => s.Id == id);
            var check = new CheckBox { Content = $"D{id}  {signal?.Name}", IsChecked = channelSet.Contains(id), FontSize = 11, Margin = new(0, 0, 8, 4) }; checks[id] = check;
            check.IsCheckedChanged += (_, _) => { if (changing) return; changing = true; if (check.IsChecked == true) channelSet.Add(id); else { channelSet.Remove(id); foreach (var d in Capture.Decoders.Where(d => d.Channels.Contains(id))) { decoderSet.Remove(d.Id); decoderChecks[d.Id].IsChecked = false; } } changing = false; }; channelGrid.Children.Add(check);
        }
        body.Children.Add(channelGrid); var error = Text(""); error.Foreground = TimelineControl.Ink("#FF9AAA"); body.Children.Add(error);
        var actions = Row(); actions.Children.Add(Button("New group", () => { dialog.Close(); EditGroup(true); })); actions.Children.Add(Button("Cancel", dialog.Close));
        actions.Children.Add(Button("Save group", () =>
        {
            string title = name.Text?.Trim() ?? "";
            if (title.Length == 0 || title == "All signals" || channelSet.Count == 0) { error.Text = "Enter a name and select at least one signal."; return; }
            if (Model.Groups.Any(g => g.Name.Equals(title, StringComparison.OrdinalIgnoreCase) && (create || !ReferenceEquals(g, original)))) { error.Text = "Choose a different group name."; return; }
            var edited = new ObservationGroup(title, channelSet.Order().ToArray(), decoderSet.ToArray());
            if (!create && original.Name != "All signals") Model.Groups[Model.Groups.IndexOf(original)] = edited; else Model.Groups.Insert(Model.Groups.Count - 1, edited);
            Model.SetGroup(edited); SavePreferences(); dialog.Close();
        }, "primary")); body.Children.Add(actions); dialog.Content = new ScrollViewer { Content = body }; await dialog.ShowDialog(this);
    }
    private async void EditColor(int channel)
    {
        var dialog = new Window { Title = $"D{channel} waveform color", Width = 390, Height = 245, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var body = new StackPanel { Margin = new(20), Spacing = 14 }; body.Children.Add(Text($"D{channel} · waveform line"));
        var input = new TextBox { Text = Model.Colors.GetValueOrDefault(channel, channel == 0 ? "#D4AD00" : "#C7D2DB"), PlaceholderText = "#RRGGBB" }; body.Children.Add(input);
        var presets = Row(); foreach (var hex in new[] { "#D4AD00", "#78D7C8", "#8BB7FF", "#E58C8C", "#C7D2DB" }) { var b = Button("", () => input.Text = hex); b.Background = TimelineControl.Ink(hex); b.Width = 42; presets.Children.Add(b); } body.Children.Add(presets);
        var actions = Row(); actions.Children.Add(Button("Use default", () => { Model.Colors.Remove(channel); Model.Notify(); SavePreferences(); dialog.Close(); })); actions.Children.Add(Button("Apply", () => { if (Color.TryParse(input.Text, out var color)) { Model.Colors[channel] = color.ToString(); Model.Notify(); SavePreferences(); dialog.Close(); } else input.PlaceholderText = "Use a valid color such as #D4AD00"; }, "primary")); body.Children.Add(actions);
        dialog.Content = body; await dialog.ShowDialog(this);
    }
    private async void ShowInfo()
    {
        var dialog = new Window { Title = "Saved demo", Width = 430, Height = 270, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        dialog.Content = new StackPanel { Margin = new(22), Spacing = 18, Children = { Text("Offline sample capture"), new TextBlock { Text = "32 channels · 50 MS/s · 1 ms\n\nWaveforms and decoded records are bundled synthetic data. Native hardware acquisition and live decoding are not connected in this development build.", TextWrapping = TextWrapping.Wrap }, Button("Close", dialog.Close) } }; await dialog.ShowDialog(this);
    }
    private void RestorePreferences()
    {
        try
        {
            if (!File.Exists(preferencePath)) return;
            var preferences = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(preferencePath)); if (preferences?.Version != 1) return;
            var valid = (preferences.Groups ?? []).Where(g => !string.IsNullOrWhiteSpace(g.Name) && g.Name.Length <= 40 && g.Name != "All signals" && g.Channels is { Length: > 0 } && g.Channels.All(c => c is >= 0 and < 32) && g.Decoders != null).Take(20).ToArray();
            if (valid.Length > 0) { Model.Groups.Clear(); foreach (var g in valid) { var ds = Capture.Decoders.Where(d => g.Decoders.Contains(d.Id)).ToArray(); Model.Groups.Add(new(g.Name, g.Channels.Concat(ds.SelectMany(d => d.Channels)).Distinct().Order().ToArray(), ds.Select(d => d.Id).ToArray())); } Model.Groups.Add(new("All signals", Enumerable.Range(0, 32).ToArray(), [])); }
            Model.SetGroup(Model.Groups.FirstOrDefault(g => g.Name == preferences.ActiveGroup) ?? Model.Groups[0]);
            foreach (var pair in preferences.Colors ?? []) if (pair.Key is >= 0 and < 32 && Color.TryParse(pair.Value, out _)) Model.Colors[pair.Key] = pair.Value;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { /* Preserve a malformed preference file; start with defaults. */ }
    }
    private void SavePreferences()
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(preferencePath)!); var temp = preferencePath + ".tmp"; File.WriteAllText(temp, JsonSerializer.Serialize(new Preferences(1, Model.Group.Name, Model.Groups.Where(g => g.Name != "All signals").ToArray(), Model.Colors), new JsonSerializerOptions { WriteIndented = true })); File.Move(temp, preferencePath, true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { status.Text = "Settings could not be saved: " + ex.Message; }
    }
    private sealed record Preferences(int Version, string ActiveGroup, ObservationGroup[] Groups, Dictionary<int, string> Colors);
    private static TextBlock Text(string value, bool muted = false) => new() { Text = value, VerticalAlignment = VerticalAlignment.Center, Foreground = TimelineControl.Ink(muted ? "#9BAEBF" : "#E4EAF0"), FontSize = muted ? 12 : 13 };
    private static StackPanel Row() => new() { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
    private static Button Button(string label, Action action, string? style = null, string? tip = null) { var button = new Button { Content = label, VerticalAlignment = VerticalAlignment.Center }; if (style != null) button.Classes.Add(style); if (tip != null) ToolTip.SetTip(button, tip); button.Click += (_, _) => action(); return button; }
    private static Border Band(Control child) => new() { BorderThickness = new(0, 0, 0, 1), BorderBrush = TimelineControl.Ink("#26343F"), Child = child };
    private static void Add(Grid grid, Control child, int row, int column = 0) { Grid.SetRow(child, row); Grid.SetColumn(child, column); grid.Children.Add(child); }
}
