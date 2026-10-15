// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using Avalonia.Platform.Storage;
using DSView.Infrastructure;

namespace DSView.Desktop;

public sealed partial class MainWindow
{
    private string? importedPath;
    private static readonly FilePickerFileType CaptureType = new("DSView capture") { Patterns = ["*.dsl", "*.demo"] };
    private async Task ImportCaptureAsync()
    {
        if (fileBusy || closePrompt) return;
        _decoder.Close();
        if (!NativeDslSource.IsConfigured)
        {
            await FileMessageAsync("Capture import unavailable", "This build does not include a configured DSView capture reader. You can still open saved DSView2 analyses."); return;
        }
        try
        {
            var chosen = await StorageProvider.OpenFilePickerAsync(new() { Title = "Import DSView capture", AllowMultiple = false, FileTypeFilter = [CaptureType] });
            if (chosen.Count == 0) return;
            string path = Path.GetFullPath(chosen[0].TryGetLocalPath() ?? throw new IOException("Choose a local capture file."));
            if (!await ConfirmReplacementAsync()) return;
            var preferences = personalPreferences ?? CurrentPreferences();
            fileBusy = true; loadCancellation?.Cancel(); loadCancellation?.Dispose(); loadCancellation = new(); int request = ++generation;
            var cancellation = loadCancellation.Token;
            SetOpening(true); status.Text = "Importing " + Path.GetFileName(path) + "…";
            var progress = new Progress<string>(text => { if (fileBusy && request == generation) status.Text = text; });
            try
            {
                var result = await NativeDslSource.LoadAsync(path, NativeDslSource.ReadConfiguration(NativeDslSource.ConfigurationPath), cancellation, progress);
                var prepared = await Task.Run(() => Workbench.PrepareValidatedAnalysis(Workbench.CreateImportedAnalysis(result, preferences.Groups, preferences.Colors, cancellation)), cancellation);
                if (request != generation || cancellation.IsCancellationRequested) return;
                // Discard permission never clears the old analysis before this validated commit.
                personalPreferences ??= preferences;
                updating = true;
                try { Model.ApplyAnalysis(prepared); }
                finally { updating = false; }
                importedPath = path; analysisPath = null; sessionSourceName = result.SourceName;
                renderedPacket = null; renderedIssue = null; ResetNumericErrors(); Refresh();
                waveScroll.Offset = default; edgeChannel.SelectedItem = $"D{result.Capture.Signals[0].Id}";
                // The raw file is unchanged; this new analysis has not been saved yet.
                baselineCapture = null; baselineState = null;
            }
            finally { fileBusy = false; SetOpening(false); UpdateSourceIdentity(); }
            waveTab.Focus();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { await FileMessageAsync("Import failed", "Current analysis was kept.\n" + FileFailureReason(ex)); }
    }
}
