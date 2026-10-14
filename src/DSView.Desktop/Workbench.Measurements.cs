// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using DSView.Domain;
using DSView.Infrastructure;

namespace DSView.Desktop;

public sealed partial class Workbench
{
    private readonly List<TimingResult> keptMeasurements = [];
    private (int Index, TimingResult Result)? removedMeasurement;
    public IReadOnlyList<TimingResult> Measurements => keptMeasurements.AsReadOnly();
    public bool CanUndoMeasurementRemoval => removedMeasurement != null;

    public TimingResult? KeepMeasurement()
    {
        if (Capture == null || A == null || B == null || HasPendingEdit) return null;
        if (keptMeasurements.Count >= TimingResult.MaximumCount)
        { MeasurementError($"This analysis can keep at most {TimingResult.MaximumCount} timing results."); return null; }
        string basis = PinnedMeasurement is { } local ? $"D{A.Channel} {(local.High ? "high" : "low")} pulse"
            : A.Channel == B.Channel ? $"D{A.Channel} interval" : $"D{A.Channel} → D{B.Channel} interval";
        string name = basis; int suffix = 2;
        while (keptMeasurements.Any(r => r.Name == name)) name = basis + " " + suffix++;
        var result = new TimingResult(Guid.NewGuid().ToString("N"), name, "", A, B, PinnedMeasurement != null);
        AnalysisFile.ValidateTimingResult(Capture, result);
        keptMeasurements.Add(result); Hint = $"Kept “{name}”"; Notify(); return result;
    }
    public bool EditMeasurement(string id, string name, string note)
    {
        int index = keptMeasurements.FindIndex(r => r.Id == id);
        if (index < 0 || !TimingResult.ValidMetadata(name, note)) return false;
        keptMeasurements[index] = keptMeasurements[index] with { Name = name.Trim(), Note = note };
        Notify(); return true;
    }
    public bool ShowMeasurement(string id)
    {
        var result = keptMeasurements.FirstOrDefault(r => r.Id == id);
        if (Capture == null || result == null || HasPendingEdit) return false;
        A = result.A; B = result.B;
        PinnedMeasurement = result.LocalPulse ? Capture.MeasureAt(A.Channel, A.Sample) : null;
        Mode = result.LocalPulse ? 0 : A.IsEdge && B.IsEdge ? 1 : 2;
        Probe = null; Hint = ""; FitMarkers(); return true;
    }
    public bool RemoveMeasurement(string id)
    {
        int index = keptMeasurements.FindIndex(r => r.Id == id); if (index < 0) return false;
        removedMeasurement = (index, keptMeasurements[index]); keptMeasurements.RemoveAt(index); Notify(); return true;
    }
    public TimingResult? UndoMeasurementRemoval()
    {
        if (removedMeasurement is not { } removed || keptMeasurements.Count >= TimingResult.MaximumCount) return null;
        keptMeasurements.Insert(Math.Min(removed.Index, keptMeasurements.Count), removed.Result);
        removedMeasurement = null; Notify(); return removed.Result;
    }
}
