// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using DSView.Checks.Support;
using DSView.Infrastructure;

internal static class AppearanceStorageChecks
{
    private const string FileName = "DSView2/appearance.json";
    public static async Task RunAsync(Action<bool, string> check)
    {
        check(MissingFileLoadsDefaults(), nameof(MissingFileLoadsDefaults));
        check(MalformedFileIsPreserved(), nameof(MalformedFileIsPreserved));
        check(UnavailableRootFailsClosed(), nameof(UnavailableRootFailsClosed));
        check(await SavedAppearanceSurvivesANewStoreAsync(), nameof(SavedAppearanceSurvivesANewStoreAsync));
        check(await InvalidChangeCannotReplaceSavedFileAsync(), nameof(InvalidChangeCannotReplaceSavedFileAsync));
        check(await FailedReplacementReportsErrorAndRemovesStagingAsync(), nameof(FailedReplacementReportsErrorAndRemovesStagingAsync));
        check(WorkspaceRejectsEscapingPath(), nameof(WorkspaceRejectsEscapingPath));
    }
    private static bool MissingFileLoadsDefaults()
    {
        using var workspace = new TestWorkspace(); var store = new LocalAppearanceStore(new(() => workspace.Root));
        return store.Load() == new AppearanceLoadResult(DisplayPreferences.Default, null);
    }
    private static bool MalformedFileIsPreserved()
    {
        using var workspace = new TestWorkspace(); workspace.Write(FileName, "not json");
        var store = new LocalAppearanceStore(new(() => workspace.Root));
        return store.Load().Warning != null && workspace.Read(FileName) == "not json";
    }
    private static bool UnavailableRootFailsClosed() => new LocalAppearanceStore(new(() => null)).Load().Warning != null;
    private static async Task<bool> SavedAppearanceSurvivesANewStoreAsync()
    {
        using var workspace = new TestWorkspace(); var store = new LocalAppearanceStore(new(() => workspace.Root));
        var chosen = new DisplayPreferences(1, 16, 18, 2.5, true);
        var saved = await store.SaveAsync(chosen);
        var reloaded = new LocalAppearanceStore(new(() => workspace.Root)).Load();
        return saved.Succeeded && reloaded.Preferences == chosen && workspace.TemporaryFiles("DSView2") == 0;
    }
    private static async Task<bool> InvalidChangeCannotReplaceSavedFileAsync()
    {
        using var workspace = new TestWorkspace(); workspace.Write(FileName, "earlier settings evidence");
        var store = new LocalAppearanceStore(new(() => workspace.Root));
        var result = await store.SaveAsync(DisplayPreferences.Default with { WaveformThickness = double.NaN });
        return !result.Succeeded && workspace.Read(FileName) == "earlier settings evidence";
    }
    private static async Task<bool> FailedReplacementReportsErrorAndRemovesStagingAsync()
    {
        using var workspace = new TestWorkspace(); workspace.MakeDirectory(FileName);
        var store = new LocalAppearanceStore(new(() => workspace.Root));
        var result = await store.SaveAsync(DisplayPreferences.Default);
        return !result.Succeeded && result.Error != null && workspace.IsDirectory(FileName) && workspace.TemporaryFiles("DSView2") == 0;
    }
    private static bool WorkspaceRejectsEscapingPath()
    {
        using var workspace = new TestWorkspace();
        try { workspace.Read("../escape.txt"); return false; }
        catch (ArgumentException) { return true; }
    }
}
