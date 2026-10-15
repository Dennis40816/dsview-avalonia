// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using DSView.Desktop;
using DSView.Infrastructure;

internal static class DisplayPreferencesChecks
{
    private const string Valid = "{\"Version\":1,\"TextSize\":14,\"DataTextSize\":17,\"WaveformThickness\":2.5,\"ReverseWheel\":true}";
    public static async Task RunAsync(Action<bool, string> check)
    {
        check(AdmitsIndependentDisplayValues(), nameof(AdmitsIndependentDisplayValues));
        check(RejectsUnknownField(), nameof(RejectsUnknownField));
        check(RejectsDuplicateField(), nameof(RejectsDuplicateField));
        check(RejectsMissingWheelField(), nameof(RejectsMissingWheelField));
        check(RejectsUnsupportedVersion(), nameof(RejectsUnsupportedVersion));
        check(RejectsSmallInterfaceText(), nameof(RejectsSmallInterfaceText));
        check(RejectsLargeDataText(), nameof(RejectsLargeDataText));
        check(RejectsZeroStroke(), nameof(RejectsZeroStroke));
        check(RejectsWrongWheelType(), nameof(RejectsWrongWheelType));
        check(RejectsMalformedDocument(), nameof(RejectsMalformedDocument));
        check(RejectsOversizedDocument(), nameof(RejectsOversizedDocument));
        check(CancelRestoresLoadedAppearance(), nameof(CancelRestoresLoadedAppearance));
        check(DefaultsRemainADraftUntilApply(), nameof(DefaultsRemainADraftUntilApply));
        check(await SuccessfulApplyIsRetainedOnReopenAsync(), nameof(SuccessfulApplyIsRetainedOnReopenAsync));
        check(await FailedSaveKeepsDraftAndPriorPreferencesAsync(), nameof(FailedSaveKeepsDraftAndPriorPreferencesAsync));
        check(await UnexpectedStoreFaultRemainsRecoverableAsync(), nameof(UnexpectedStoreFaultRemainsRecoverableAsync));
        check(await PendingSavePreventsCancelAndFurtherEditsAsync(), nameof(PendingSavePreventsCancelAndFurtherEditsAsync));
    }
    private static bool AdmitsIndependentDisplayValues() => DisplayPreferences.Admit(Valid) is
        { Warning: null, Preferences: { TextSize: 14, DataTextSize: 17, WaveformThickness: 2.5, ReverseWheel: true } };
    private static bool Rejected(string value) => DisplayPreferences.Admit(value) is { Warning: not null };
    private static bool RejectsUnknownField() => Rejected(Valid.Replace("}", ",\"Extra\":0}", StringComparison.Ordinal));
    private static bool RejectsDuplicateField() => Rejected(Valid.Replace("}", ",\"TextSize\":15}", StringComparison.Ordinal));
    private static bool RejectsMissingWheelField() => Rejected(Valid.Replace(",\"ReverseWheel\":true", "", StringComparison.Ordinal));
    private static bool RejectsUnsupportedVersion() => Rejected(Valid.Replace("\"Version\":1", "\"Version\":2", StringComparison.Ordinal));
    private static bool RejectsSmallInterfaceText() => Rejected(Valid.Replace("\"TextSize\":14", "\"TextSize\":11", StringComparison.Ordinal));
    private static bool RejectsLargeDataText() => Rejected(Valid.Replace("\"DataTextSize\":17", "\"DataTextSize\":19", StringComparison.Ordinal));
    private static bool RejectsZeroStroke() => Rejected(Valid.Replace("\"WaveformThickness\":2.5", "\"WaveformThickness\":0", StringComparison.Ordinal));
    private static bool RejectsWrongWheelType() => Rejected(Valid.Replace("true", "\"yes\"", StringComparison.Ordinal));
    private static bool RejectsMalformedDocument() => Rejected("{");
    private static bool RejectsOversizedDocument() => Rejected(new string(' ', 16_385));
    private static bool CancelRestoresLoadedAppearance()
    {
        var model = new AppearanceSettingsViewModel(new AppearanceStoreStub()); model.OpenCommand.Execute(null);
        model.TextSize = 16; model.CancelCommand.Execute(null); model.OpenCommand.Execute(null);
        return model.TextSize == 14 && model.DataTextSize == 17;
    }
    private static bool DefaultsRemainADraftUntilApply()
    {
        var store = new AppearanceStoreStub(); var model = new AppearanceSettingsViewModel(store); model.OpenCommand.Execute(null);
        model.RestoreDefaultsCommand.Execute(null);
        return model.Current == DisplayPreferences.Default && store.Saved == null && model.IsOpen;
    }
    private static async Task<bool> SuccessfulApplyIsRetainedOnReopenAsync()
    {
        var store = new AppearanceStoreStub(); var model = new AppearanceSettingsViewModel(store); model.OpenCommand.Execute(null); model.TextSize = 16;
        await model.ApplyCommand.ExecuteAsync(null); model.OpenCommand.Execute(null);
        return store.Saved?.TextSize == 16 && model.TextSize == 16;
    }
    private static async Task<bool> FailedSaveKeepsDraftAndPriorPreferencesAsync()
    {
        var store = new AppearanceStoreStub { Save = _ => Task.FromResult(AppearanceSaveResult.Failed) };
        var model = new AppearanceSettingsViewModel(store); model.OpenCommand.Execute(null); model.TextSize = 16;
        await model.ApplyCommand.ExecuteAsync(null);
        bool draftRetained = model.IsOpen && model.TextSize == 16 && model.Message != null;
        model.CancelCommand.Execute(null); model.OpenCommand.Execute(null);
        return draftRetained && model.TextSize == 14;
    }
    private static async Task<bool> UnexpectedStoreFaultRemainsRecoverableAsync()
    {
        var store = new AppearanceStoreStub { Save = _ => Task.FromException<AppearanceSaveResult>(new InvalidOperationException("Injected adapter fault")) };
        var model = new AppearanceSettingsViewModel(store); model.OpenCommand.Execute(null);
        await model.ApplyCommand.ExecuteAsync(null);
        return model.IsOpen && model.Message != null && model.CancelCommand.CanExecute(null);
    }
    private static async Task<bool> PendingSavePreventsCancelAndFurtherEditsAsync()
    {
        var completion = new TaskCompletionSource<AppearanceSaveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new AppearanceStoreStub { Save = _ => completion.Task };
        var model = new AppearanceSettingsViewModel(store); model.OpenCommand.Execute(null); model.TextSize = 16;
        Task saving = model.ApplyCommand.ExecuteAsync(null);
        model.DataTextSize = 12;
        bool protectedDraft = model.IsSaving && !model.CancelCommand.CanExecute(null) && !model.ApplyCommand.CanExecute(null) && model.DataTextSize == 17;
        completion.SetResult(AppearanceSaveResult.Saved); await saving;
        return protectedDraft && !model.IsOpen && !model.IsSaving;
    }
    // One fake for this family. No real filesystem, timers, UI setup or global state.
    private sealed class AppearanceStoreStub : IAppearanceStore
    {
        public Func<DisplayPreferences, Task<AppearanceSaveResult>>? Save { get; init; }
        public DisplayPreferences? Saved { get; private set; }
        public AppearanceLoadResult Load() => DisplayPreferences.Admit(Valid);
        public Task<AppearanceSaveResult> SaveAsync(DisplayPreferences preferences)
        {
            if (Save != null) return Save(preferences);
            Saved = preferences; return Task.FromResult(AppearanceSaveResult.Saved);
        }
    }
}
