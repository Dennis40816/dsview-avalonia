// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using DSView.Desktop;
using DSView.Domain;

internal static class CursorPlacementChecks
{
    public static void Run(Action<bool, string> check)
    {
        check(CrossChannelDragCommitsReleasePosition(), nameof(CrossChannelDragCommitsReleasePosition));
        check(FreePositionDragPreservesSignedInterval(), nameof(FreePositionDragPreservesSignedInterval));
        check(CancelRestoresLocalPulseAndCycle(), nameof(CancelRestoresLocalPulseAndCycle));
        check(InvalidReleaseRestoresOriginalInsteadOfLastPreview(), nameof(InvalidReleaseRestoresOriginalInsteadOfLastPreview));
        check(ReleaseOutsideSignalsRestoresOriginal(), nameof(ReleaseOutsideSignalsRestoresOriginal));
        check(UnobservedEdgeCannotStartDrag(), nameof(UnobservedEdgeCannotStartDrag));
        check(UnobservedEdgeCannotFinishDrag(), nameof(UnobservedEdgeCannotFinishDrag));
        check(ReplacementCaptureRejectsStaleGesture(), nameof(ReplacementCaptureRejectsStaleGesture));
        check(ExplicitSingleEndpointChoiceDoesNotStartPair(), nameof(ExplicitSingleEndpointChoiceDoesNotStartPair));
        check(PreviewCannotBeKeptOrPersistedAsCommitted(), nameof(PreviewCannotBeKeptOrPersistedAsCommitted));
    }

    private static Capture Source() => new(1_000_000, 200,
        [new(0, "Clock", false, [new(10, true), new(30, false), new(60, true)]),
         new(8, "Interrupt", false, [new(45, true), new(90, false)])], []);

    private static Workbench Model(Capture? source = null)
    {
        var model = new Workbench(); model.Load(source ?? Source()); model.FitTime();
        model.Pick(0, 20, 0); return model;
    }

    private static bool CrossChannelDragCommitsReleasePosition()
    {
        var model = Model(); model.SetMode(1); var drag = new CursorPlacementGesture(model);
        bool began = drag.Begin(0, 11, 2); drag.Move(8, 44, 2);
        bool ended = drag.Complete(8, 91, 2);
        return began && ended && model.A is { Channel: 0, Sample: 10, IsEdge: true }
            && model.B is { Channel: 8, Sample: 90, IsEdge: true } && !model.HasPendingEdit
            && model.PinnedMeasurement == null && model.Start == 0 && model.End == 200;
    }

    private static bool FreePositionDragPreservesSignedInterval()
    {
        var model = Model(); model.SetMode(2); var drag = new CursorPlacementGesture(model);
        return drag.Begin(8, 83, 0) && drag.Complete(0, 17, 0)
            && model.A is { Channel: 8, Sample: 83, IsEdge: false }
            && model.B is { Channel: 0, Sample: 17, IsEdge: false }
            && model.B.Sample - model.A.Sample == -66 && model.PinnedMeasurement == null;
    }

    private static bool CancelRestoresLocalPulseAndCycle()
    {
        var model = Model(); var origin = (model.A, model.B, model.PinnedMeasurement, model.Mode);
        model.SetMode(1); var drag = new CursorPlacementGesture(model);
        drag.Begin(0, 60, 0); drag.Move(8, 90, 0); drag.Cancel();
        return (model.A, model.B, model.PinnedMeasurement, model.Mode) == origin
            && model.PinnedMeasurement?.PeriodSamples == 50 && !model.HasPendingEdit && !drag.IsActive;
    }

    private static bool InvalidReleaseRestoresOriginalInsteadOfLastPreview()
    {
        var model = Model(); var original = (model.A, model.B, model.PinnedMeasurement);
        model.SetMode(1); var drag = new CursorPlacementGesture(model);
        drag.Begin(0, 60, 0); drag.Move(8, 90, 0);
        return !drag.Complete(8, 120, 2) && (model.A, model.B, model.PinnedMeasurement) == original
            && !model.HasPendingEdit && model.Hint.Contains("unchanged");
    }

    private static bool ReleaseOutsideSignalsRestoresOriginal()
    {
        var model = Model(); var original = (model.A, model.B);
        model.SetMode(2); var drag = new CursorPlacementGesture(model); drag.Begin(0, 17, 0);
        return !drag.Complete(null, 80, 0) && (model.A, model.B) == original && !model.HasPendingEdit;
    }

    private static bool UnobservedEdgeCannotStartDrag()
    {
        var model = Model(Source() with { Gaps = [new(60, 70)] }); var original = (model.A, model.B);
        model.SetMode(1); var drag = new CursorPlacementGesture(model);
        return !drag.Begin(0, 60, 2) && !drag.IsActive && model.Armed == 0 && (model.A, model.B) == original;
    }

    private static bool UnobservedEdgeCannotFinishDrag()
    {
        var model = Model(Source() with { Gaps = [new(90, 100)] }); var original = (model.A, model.B);
        model.SetMode(1); var drag = new CursorPlacementGesture(model); drag.Begin(0, 10, 0);
        return !drag.Complete(8, 90, 2) && (model.A, model.B) == original && !model.HasPendingEdit;
    }

    private static bool ReplacementCaptureRejectsStaleGesture()
    {
        var model = Model(); model.SetMode(1); var drag = new CursorPlacementGesture(model); drag.Begin(0, 10, 0);
        model.Load(Source()); model.SetMode(2); model.Pick(8, 100, 0);
        bool rejected = !drag.Complete(0, 30, 0); drag.Cancel();
        return rejected && model.A is { Channel: 8, Sample: 100, IsEdge: false } && model.Armed == 1;
    }

    private static bool ExplicitSingleEndpointChoiceDoesNotStartPair()
    {
        var model = Model(); var original = (model.A, model.B); model.Arm(0);
        return !new CursorPlacementGesture(model).Begin(8, 45, 0) && (model.A, model.B) == original && model.Armed == 0;
    }

    private static bool PreviewCannotBeKeptOrPersistedAsCommitted()
    {
        var model = Model(); var original = (model.A, model.B);
        model.SetMode(1); var drag = new CursorPlacementGesture(model); drag.Begin(0, 60, 0); drag.Move(8, 90, 0);
        var saved = model.SnapshotState(new(null, 0), 0);
        return model.KeepMeasurement() == null && model.HasPendingEdit && (saved.A, saved.B) == original;
    }
}
