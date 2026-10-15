// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using Avalonia.Media;
using DSView.Desktop;
using DSView.Domain;

internal static class ChannelAppearanceChecks
{
    public static void Run(Action<bool, string> check)
    {
        check(CancelDiscardsPresetAndDefaultDrafts(), nameof(CancelDiscardsPresetAndDefaultDrafts));
        check(ApplyChangesOnlySelectedWaveform(), nameof(ApplyChangesOnlySelectedWaveform));
        check(ResetRemovesOverrideOnlyOnApply(), nameof(ResetRemovesOverrideOnlyOnApply));
        check(InvalidInputCannotCommitOrPublish(), nameof(InvalidInputCannotCommitOrPublish));
        check(ReplacementCaptureCannotReceiveAnOldDraft(), nameof(ReplacementCaptureCannotReceiveAnOldDraft));
        check(ClosedEditorCannotPublishAgain(), nameof(ClosedEditorCannotPublishAgain));
        check(LegacyOpaqueColorCanBeEditedWithoutOpeningMutation(), nameof(LegacyOpaqueColorCanBeEditedWithoutOpeningMutation));
    }

    private static Workbench Model()
    {
        var model = new Workbench();
        model.Load(new Capture(1_000_000, 200,
            [new(0, "SCL", false, [new(10, true), new(30, false), new(60, true)]),
             new(8, "Interrupt", false, [new(45, true), new(90, false)])], []));
        model.Pick(0, 20, 0); model.Colors[8] = "#123456"; return model;
    }
    private static ChannelAppearanceViewModel Editor(Workbench model) => new(model, model.Capture!.Signals[1]);

    private static bool CancelDiscardsPresetAndDefaultDrafts()
    {
        var model = Model(); var editor = Editor(model); int published = 0; editor.Applied += () => published++;
        editor.SelectColorCommand.Execute("#D4AD00");
        bool previewOnly = model.Colors[8] == "#123456" && ((ISolidColorBrush)editor.PreviewBrush).Color == Color.Parse("#D4AD00");
        editor.DefaultCommand.Execute(null); editor.CancelCommand.Execute(null);
        return previewOnly && model.Colors[8] == "#123456" && !editor.IsOpen && published == 0;
    }

    private static bool ApplyChangesOnlySelectedWaveform()
    {
        var model = Model(); var capture = model.Capture; var timing = (model.A, model.B, model.PinnedMeasurement);
        var editor = Editor(model); var probe = editor.ProbeBrush; int published = 0; editor.Applied += () => published++;
        editor.ColorText = "#abcdef"; editor.ApplyCommand.Execute(null);
        return model.Colors[8] == "#ABCDEF" && !model.Colors.ContainsKey(0) && published == 1
            && ReferenceEquals(capture, model.Capture) && (model.A, model.B, model.PinnedMeasurement) == timing
            && ReferenceEquals(probe, editor.ProbeBrush) && !editor.IsOpen;
    }

    private static bool ResetRemovesOverrideOnlyOnApply()
    {
        var model = Model(); var editor = Editor(model); editor.DefaultCommand.Execute(null);
        bool preserved = model.Colors[8] == "#123456"; editor.ApplyCommand.Execute(null);
        return preserved && !model.Colors.ContainsKey(8);
    }

    private static bool InvalidInputCannotCommitOrPublish()
    {
        foreach (var input in new[] { "", "red", "#123", "#GG2233", "#00112233", "#1234567" })
        {
            var model = Model(); var editor = Editor(model); int published = 0; editor.Applied += () => published++;
            editor.ColorText = input; bool disabled = !editor.ApplyCommand.CanExecute(null); editor.ApplyCommand.Execute(null);
            if (!disabled || !editor.HasError || model.Colors[8] != "#123456" || published != 0 || !editor.IsOpen) return false;
        }
        return true;
    }

    private static bool ReplacementCaptureCannotReceiveAnOldDraft()
    {
        var model = Model(); var editor = Editor(model); editor.ColorText = "#ABCDEF";
        model.Load(model.Capture! with { SampleRate = 2_000_000 }); var original = model.Colors[8];
        bool disabled = !editor.ApplyCommand.CanExecute(null); editor.ApplyCommand.Execute(null);
        return disabled && model.Colors[8] == original;
    }

    private static bool ClosedEditorCannotPublishAgain()
    {
        var model = Model(); var editor = Editor(model); int published = 0; editor.Applied += () => published++;
        editor.ColorText = "#AABBCC"; editor.ApplyCommand.Execute(null);
        editor.SelectColorCommand.Execute("#D4AD00"); editor.DefaultCommand.Execute(null); editor.ApplyCommand.Execute(null);
        return model.Colors[8] == "#AABBCC" && published == 1 && !editor.ApplyCommand.CanExecute(null);
    }

    private static bool LegacyOpaqueColorCanBeEditedWithoutOpeningMutation()
    {
        var model = Model(); model.Colors[8] = "#FFABCDEF"; var editor = Editor(model);
        return editor.ColorText == "#ABCDEF" && !editor.HasError && model.Colors[8] == "#FFABCDEF";
    }
}
