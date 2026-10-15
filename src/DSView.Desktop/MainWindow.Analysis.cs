// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using DSView.Domain;
using DSView.Infrastructure;
using System.Text.Json;

namespace DSView.Desktop;

public sealed partial class MainWindow
{
    private bool fileBusy, closePrompt, closeApproved, openingFile;
    private string? analysisPath, baselineState;
    private string sessionSourceName = "";
    private Capture? baselineCapture;
    private Preferences? personalPreferences;
    private readonly DispatcherTimer titleUpdate = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private static readonly FilePickerFileType AnalysisType = new("DSView2 analysis") { Patterns = ["*.dsview2"] };

    private AnalysisState CurrentAnalysisState() => Model.SnapshotState(timeline.SaveAnchor(waveScroll.Offset.Y), edgeChannel.SelectedItem is string channel && int.TryParse(channel.AsSpan(1), out int id) ? id : null);
    private bool NumericDraft => Model.Capture is { } capture && ((aTime.IsFocused && Model.A is { } a && aTime.Text != TimeAxis.Microseconds(a.Sample, capture.SampleRate)) || (bTime.IsFocused && Model.B is { } b && bTime.Text != TimeAxis.Microseconds(b.Sample, capture.SampleRate)));
    private bool HasAnalysisChanges => Model.Capture != null && (Model.HasPendingEdit || NumericDraft || invalidTimes.Count > 0 || !ReferenceEquals(Model.Capture, baselineCapture) || AnalysisFile.StateKey(CurrentAnalysisState()) != baselineState);
    private Preferences CurrentPreferences() => new(1, Model.Group.Name, Model.Groups.Where(g => g.Name != "All signals").Select(g => new ObservationGroup(g.Name, g.Channels.ToArray(), g.Decoders.ToArray())).ToArray(), new(Model.Colors));
    private void RestorePersonalSettings()
    {
        if (personalPreferences is not { } preferences) return;
        updating = true;
        try
        {
            Model.Groups.Clear(); foreach (var group in preferences.Groups) Model.Groups.Add(group);
            Model.Groups.Add(new("All signals", Enumerable.Range(0, 32).ToArray(), []));
            Model.SetGroup(Model.Groups.FirstOrDefault(g => g.Name == preferences.ActiveGroup) ?? Model.Groups[0]);
            Model.Colors.Clear(); foreach (var pair in preferences.Colors) Model.Colors.Add(pair.Key, pair.Value);
            personalPreferences = null;
        }
        finally { updating = false; }
    }
    private void UpdateSourceIdentity()
    {
        status.Text = Model.Capture is { } capture ? SourceSummary(capture) : "No capture";
        ToolTip.SetTip(status, analysisPath ?? importedPath);
        UpdateAnalysisTitle();
    }
    private void UpdateAnalysisTitle() => Title = "DSView2 · " + (analysisPath != null ? Path.GetFileName(analysisPath) : importedPath != null ? Path.GetFileName(importedPath) : "Offline demo") + (HasAnalysisChanges ? " *" : "");
    private void ScheduleTitleUpdate() { if (!titleUpdate.IsEnabled) titleUpdate.Start(); }
    private void SetOpening(bool opening)
    {
        openingFile = opening;
        _shell.Body.IsEnabled = !opening;
        foreach (var control in _shell.Header.Children)
            control.IsEnabled = !opening || ReferenceEquals(control, reloadButton);
    }
    private void ShowFiles()
    {
        var body = new StackPanel { Spacing = 6, MinWidth = 240 };
        void Command(string text, Action action, bool enabled = true)
        {
            var item = Button(text, () => { openPanel?.Hide(); openPanel = null; action(); }, "flat");
            item.HorizontalAlignment = HorizontalAlignment.Stretch; item.HorizontalContentAlignment = HorizontalAlignment.Left; item.IsEnabled = enabled; body.Children.Add(item);
        }
        if (fileBusy) Command("Cancel file operation", () => loadCancellation?.Cancel());
        else
        {
            Command("Open analysis…     Ctrl+O", () => _ = OpenAnalysisAsync());
            Command("Import DSView capture…", () => _ = ImportCaptureAsync());
            Command("Save analysis       Ctrl+S", () => _ = SaveAnalysisAsync(false), Model.Capture != null);
            Command("Save analysis as…  Ctrl+Shift+S", () => _ = SaveAnalysisAsync(true), Model.Capture != null);
            body.Children.Add(new Separator());
            if (analysisPath == null && importedPath == null) Command("Reload example", () => _ = LoadAsync(activeExample));
            Command("Capture source…", ShowInfo, Model.Capture != null);
        }
        openPanel = new Flyout { Content = body }; openPanel.FlyoutPresenterClasses.Add("analysis"); openPanel.ShowAt(reloadButton);
    }
    private async Task OpenAnalysisAsync()
    {
        if (fileBusy || closePrompt) return;
        _decoder.Close();
        try
        {
            var chosen = await StorageProvider.OpenFilePickerAsync(new() { Title = "Open DSView2 analysis", AllowMultiple = false, FileTypeFilter = [AnalysisType] });
            if (chosen.Count == 0) return;
            string path = chosen[0].TryGetLocalPath() ?? throw new IOException("Choose a local analysis file.");
            if (!await ConfirmReplacementAsync()) return;
            fileBusy = true; loadCancellation?.Cancel(); loadCancellation?.Dispose(); loadCancellation = new(); int request = ++generation;
            SetOpening(true);
            status.Text = "Opening " + Path.GetFileName(path) + "…";
            try
            {
                var document = await AnalysisFile.ReadAsync(path, loadCancellation.Token);
                var prepared = await Task.Run(() => Workbench.PrepareValidatedAnalysis(document), loadCancellation.Token);
                if (request != generation || loadCancellation.IsCancellationRequested) return;
                personalPreferences ??= CurrentPreferences();
                updating = true;
                try { Model.ApplyAnalysis(prepared); }
                finally { updating = false; }
                analysisPath = path; importedPath = null; sessionSourceName = document.SourceName;
                renderedPacket = null; renderedIssue = null; ResetNumericErrors(); Refresh();
                edgeChannel.SelectedItem = document.State.EdgeChannel is int id ? $"D{id}" : null;
                baselineCapture = document.Capture; baselineState = AnalysisFile.StateKey(document.State);
                int reveal = ++revealGeneration;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (request != generation || reveal != revealGeneration || !ReferenceEquals(Model.Capture, document.Capture)) return;
                    waveScroll.Offset = new Vector(0, timeline.RestoreAnchor(document.State.Vertical, waveScroll.Viewport.Height, waveScroll.Extent.Height));
                    timeline.SetViewport(waveScroll.Offset.Y, waveScroll.Viewport.Height); UpdateMeasurementContext();
                    // Layout may clamp the saved anchor to this window's viewport.
                    baselineState = AnalysisFile.StateKey(CurrentAnalysisState());
                }, DispatcherPriority.Loaded);
            }
            finally { fileBusy = false; SetOpening(false); UpdateSourceIdentity(); }
            if (Model.PacketsTab) packetTab.Focus(); else waveTab.Focus();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { await FileMessageAsync("Open failed", "Current analysis was kept.\n" + FileFailureReason(ex)); }
    }
    private async Task<bool> SaveAnalysisAsync(bool saveAs)
    {
        if (fileBusy || Model.Capture == null) return false;
        _decoder.Close();
        // Keyboard Save does not move focus; commit visible numeric input before taking a snapshot.
        if (aTime.IsFocused) CommitTime(0, aTime);
        if (bTime.IsFocused) CommitTime(1, bTime);
        if (Model.HasPendingEdit || invalidTimes.Count > 0)
        {
            await FileMessageAsync("Finish the measurement edit", "Finish or cancel the current measurement edit before saving. Your committed measurement is unchanged."); return false;
        }
        try
        {
            string? path = saveAs ? null : analysisPath;
            if (path == null)
            {
                var chosen = await StorageProvider.SaveFilePickerAsync(new() { Title = "Save DSView2 analysis", DefaultExtension = "dsview2", FileTypeChoices = [AnalysisType], ShowOverwritePrompt = true, SuggestedFileName = analysisPath == null ? "analysis.dsview2" : Path.GetFileName(analysisPath) });
                if (chosen == null) return false;
                path = chosen.TryGetLocalPath() ?? throw new IOException("Choose a local destination for the analysis file.");
            }
            if (importedPath != null && string.Equals(Path.GetFullPath(path), importedPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Choose a different filename for the analysis. The imported capture must be kept intact.");
            var snapshot = new AnalysisDocument(AnalysisDocument.FileFormat, AnalysisDocument.CurrentVersion,
                CurrentSourceName, Model.Capture, CurrentAnalysisState());
            fileBusy = true; loadCancellation?.Cancel(); loadCancellation?.Dispose(); loadCancellation = new();
            status.Text = "Saving " + Path.GetFileName(path) + "…";
            try
            {
                await AnalysisFile.WriteAsync(path, snapshot, loadCancellation.Token);
                personalPreferences ??= CurrentPreferences();
                analysisPath = path; sessionSourceName = snapshot.SourceName;
                baselineCapture = snapshot.Capture; baselineState = AnalysisFile.StateKey(snapshot.State);
            }
            finally { fileBusy = false; UpdateSourceIdentity(); }
            if (HasAnalysisChanges) await FileMessageAsync("Earlier view saved", "The file contains the view captured when saving started. Your newer changes are still unsaved.");
            return !HasAnalysisChanges;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex) { await FileMessageAsync("Save failed", "Your analysis and any previous file were kept.\n" + FileFailureReason(ex)); return false; }
    }
    private static string FileFailureReason(Exception error) => error switch
    {
        JsonException => "This file is incomplete or is not a supported DSView2 analysis. Choose another analysis file.",
        FileNotFoundException or DirectoryNotFoundException => "The file or folder is no longer available. Choose an existing location.",
        UnauthorizedAccessException => "This location cannot be accessed. Choose a folder you can read and write.",
        InvalidDataException => error.Message,
        TimeoutException => error.Message,
        IOException => "The file could not be read or written. Check that it is available, that another app is not using it, and that there is enough free space.",
        _ => "The file operation could not finish. Try again or choose another location."
    };
    private async Task<bool> ConfirmReplacementAsync()
    {
        if (!HasAnalysisChanges) return true;
        int choice = await FileQuestionAsync("Keep your analysis?", "Your view or analysis has changed. Save it before leaving?", ["Cancel", "Discard changes", "Save analysis"]);
        return choice == 1 || (choice == 2 && await SaveAnalysisAsync(false));
    }
    private async void ClosingAnalysis(object? sender, WindowClosingEventArgs e)
    {
        _decoder.Close();
        if (closeApproved) { SavePreferences(); return; }
        e.Cancel = true;
        if (closePrompt || _settingsView.Model.IsSaving) return;
        if (_settingsView.Model.CancelCommand.CanExecute(null)) _settingsView.Model.CancelCommand.Execute(null);
        closePrompt = true;
        try
        {
            await _decoder.StopAsync();
            if (fileBusy) { loadCancellation?.Cancel(); await FileMessageAsync("Stopping file operation", "Close the window again after the file operation stops."); return; }
            if (await ConfirmReplacementAsync()) { closeApproved = true; Close(); }
        }
        finally { closePrompt = false; }
    }
    private Task FileMessageAsync(string title, string message) => FileQuestionAsync(title, message, ["Close"]);
    private async Task<int> FileQuestionAsync(string title, string message, string[] choices)
    {
        var dialog = new Window { Title = title, Width = 490, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var body = new StackPanel { Spacing = 16, Margin = new(20) };
        body.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxHeight = 240 });
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        for (int i = 0; i < choices.Length; i++)
        {
            int result = i; var button = Button(choices[i], () => dialog.Close(result), i == choices.Length - 1 ? "primary" : null);
            button.Margin = new(0, 0, 8, 0); if (i == 0) button.IsCancel = true; actions.Children.Add(button);
        }
        body.Children.Add(actions); dialog.Content = body;
        return await dialog.ShowDialog<int>(this);
    }
}
