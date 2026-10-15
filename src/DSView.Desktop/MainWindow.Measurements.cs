// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using DSView.Domain;

namespace DSView.Desktop;

public sealed partial class MainWindow
{
    private bool FinishMeasurementInput()
    {
        // Keyboard activation may not have moved focus away from a numeric draft.
        if (aTime.IsFocused) CommitTime(0, aTime);
        if (bTime.IsFocused) CommitTime(1, bTime);
        if (!Model.HasPendingEdit && invalidTimes.Count == 0) return true;
        Model.MeasurementError("Finish or cancel the current measurement edit first.");
        return false;
    }
    private void KeepTimingResult()
    {
        if (FinishMeasurementInput()) Model.KeepMeasurement();
    }
    private string ResultValue(TimingResult result)
    {
        var capture = Model.Capture!;
        return result.LocalPulse ? TimingText.Local(capture.MeasureAt(result.A.Channel, result.A.Sample), capture.SampleRate)
            : $"B − A   {(result.B.Sample > result.A.Sample ? "+" : "")}{TimingText.Duration((result.B.Sample - result.A.Sample) / (double)capture.SampleRate)}";
    }
    private string ResultSource(Endpoint point, string label) => $"{label} · D{point.Channel} {Model.Capture!.Signals.First(s => s.Id == point.Channel).Name} · {(point.IsEdge ? "Edge" : "Position")} · {TimeAxis.Microseconds(point.Sample, Model.Capture.SampleRate)} µs · sample {point.Sample}";

    private async Task ShowTimingResultsAsync()
    {
        if (Model.Capture == null) return;
        var dialog = new Window { Title = "Kept timing results", Width = 740, Height = 570, MinWidth = 620, MinHeight = 450, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var layout = new Grid { RowDefinitions = new("Auto,*,*,Auto,Auto"), Margin = new(20), RowSpacing = 12 };
        Add(layout, Text("Kept timing results"), 0);
        var list = new ListBox { Background = Brushes.Transparent, Focusable = true };
        AutomationProperties.SetName(list, "Kept timing results");
        list.ItemTemplate = new FuncDataTemplate<TimingResult>((item, _) =>
        {
            var row = new StackPanel { Spacing = 4, Margin = new(0, 3) };
            var name = Text(item?.Name ?? ""); name.TextWrapping = TextWrapping.Wrap; row.Children.Add(name);
            var value = Text(item == null ? "" : ResultValue(item), true); value.TextWrapping = TextWrapping.Wrap; row.Children.Add(value); return row;
        });
        Add(layout, list, 1);
        var details = new StackPanel { Spacing = 8 };
        Add(layout, new ScrollViewer { Content = details, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled }, 2);
        var message = Text("", true); message.TextWrapping = TextWrapping.Wrap;
        var feedback = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 10 }; Add(feedback, message, 0, 0);
        bool shown = false;
        Button? undo = null, show = null, edit = null, remove = null;
        void Reload(string? id)
        {
            list.ItemsSource = Model.Measurements.ToArray(); list.SelectedItem = Model.Measurements.FirstOrDefault(r => r.Id == id) ?? Model.Measurements.FirstOrDefault();
            undo!.IsVisible = Model.CanUndoMeasurementRemoval; Render();
        }
        undo = Button("Undo remove", () => { var restored = Model.UndoMeasurementRemoval(); Reload(restored?.Id); message.Text = restored == null ? "No result restored." : "Restored “" + restored.Name + "”"; list.Focus(); }, "flat");
        Add(feedback, undo, 0, 1); Add(layout, feedback, 3);
        var actions = Row(); actions.Spacing = 8;
        show = Button("Show on waveform", () =>
        {
            if (!FinishMeasurementInput()) { message.Text = "Close this list, then finish or cancel the current measurement edit."; return; }
            if (list.SelectedItem is TimingResult item && Model.ShowMeasurement(item.Id)) { shown = true; dialog.Close(); }
        }, "primary");
        edit = Button("Edit name / note", async () =>
        {
            if (list.SelectedItem is not TimingResult item) return;
            if (await EditTimingResultAsync(dialog, item)) Reload(item.Id);
        }, "flat");
        remove = Button("Remove", () =>
        {
            if (list.SelectedItem is not TimingResult item || !Model.RemoveMeasurement(item.Id)) return;
            Reload(null); message.Text = "Removed “" + item.Name + "”"; if (Model.Measurements.Count == 0) undo.Focus(); else list.Focus();
        }, "flat");
        var close = Button("Close", () => dialog.Close(), "flat"); close.IsCancel = true;
        foreach (var action in new[] { show, edit, remove, close }) actions.Children.Add(action); Add(layout, actions, 4);
        void Render()
        {
            details.Children.Clear(); var selected = list.SelectedItem as TimingResult;
            show!.IsEnabled = edit!.IsEnabled = remove!.IsEnabled = selected != null;
            if (selected == null) { details.Children.Add(Wrapped("No kept results. Measure a pulse or A/B interval, then choose Keep result.", true)); return; }
            details.Children.Add(Wrapped(ResultSource(selected.A, "A"), true)); details.Children.Add(Wrapped(ResultSource(selected.B, "B"), true));
            if (selected.LocalPulse)
            {
                var local = Model.Capture.MeasureAt(selected.A.Channel, selected.A.Sample);
                details.Children.Add(Wrapped(local.Cycle is { } cycle
                    ? $"Local cycle for T/f · {TimeAxis.FormatRange(cycle.Start, cycle.End, Model.Capture.SampleRate)} · samples {cycle.Start}–{cycle.End}"
                    : local.Reason ?? "T/f unavailable", true));
            }
            if (Model.Capture.Gaps.Any(g => g.Start <= Math.Max(selected.A.Sample, selected.B.Sample) && g.End > Math.Min(selected.A.Sample, selected.B.Sample)))
                details.Children.Add(Wrapped("Missing samples in this interval. Positions describe time, not observed signal behavior.", true));
            details.Children.Add(Wrapped(selected.Note.Length == 0 ? "No note" : selected.Note, selected.Note.Length == 0));
        }
        list.SelectionChanged += (_, _) => { message.Text = ""; Render(); };
        dialog.Content = layout; Reload(null); dialog.Opened += (_, _) => list.Focus();
        await dialog.ShowDialog(this); if (shown) waveTab.Focus(); else if (_shell.Results.IsVisible) _shell.Results.Focus(); else measureButton.Focus();
    }
    private async Task<bool> EditTimingResultAsync(Window owner, TimingResult result)
    {
        var dialog = new Window { Title = "Edit timing result", Width = 510, Height = 390, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var body = new Grid { RowDefinitions = new("Auto,Auto,Auto,*,Auto,Auto"), Margin = new(20), RowSpacing = 10 };
        var name = new TextBox { Text = result.Name, MaxLength = TimingResult.MaximumName };
        var note = new TextBox { Text = result.Note, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxLength = TimingResult.MaximumNote };
        AutomationProperties.SetName(name, "Result name"); AutomationProperties.SetName(note, "Result note");
        Add(body, Text("Name", true), 0); Add(body, name, 1); Add(body, Text("Note", true), 2); Add(body, note, 3);
        var error = Wrapped("", true); Add(body, error, 4);
        var actions = Row(); actions.Spacing = 8;
        var cancel = Button("Cancel", () => dialog.Close(false), "flat"); cancel.IsCancel = true; actions.Children.Add(cancel);
        actions.Children.Add(Button("Save changes", () =>
        {
            if (Model.EditMeasurement(result.Id, name.Text ?? "", note.Text ?? "")) dialog.Close(true);
            else error.Text = "Enter a name without control characters (up to 80 characters). Note limit: 2048.";
        }, "primary")); Add(body, actions, 5);
        dialog.Content = body; dialog.Opened += (_, _) => { name.Focus(); name.SelectAll(); }; return await dialog.ShowDialog<bool>(owner);
    }
}
