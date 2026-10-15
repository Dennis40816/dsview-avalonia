// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using DSView.Domain;

namespace DSView.Desktop;

// One pointer gesture. Workbench remains the owner of endpoints and rollback state.
internal sealed class CursorPlacementGesture(Workbench model)
{
    private Capture? capture;
    public bool IsActive => capture != null && ReferenceEquals(capture, model.Capture) && model.HasPendingEdit;

    public bool Begin(int channel, long sample, double tolerance)
    {
        if (IsActive || !model.IsChoosingPair || !model.Pick(channel, sample, tolerance)) return false;
        model.BeginDrag();
        capture = model.Capture;
        return true;
    }

    public bool Move(int channel, long sample, double tolerance) =>
        IsActive && model.Pick(channel, sample, tolerance, 1);

    public bool Complete(int? channel, long sample, double tolerance)
    {
        if (!IsActive) { capture = null; return false; }
        bool accepted = channel is int id && Move(id, sample, tolerance);
        if (accepted) model.FinishDrag();
        else
        {
            model.CancelPick();
            model.MeasurementError("Interval unchanged. Release on a signal at a valid edge or position.");
        }
        capture = null;
        return accepted;
    }

    public void Cancel()
    {
        if (IsActive) model.CancelPick();
        capture = null;
    }
}
