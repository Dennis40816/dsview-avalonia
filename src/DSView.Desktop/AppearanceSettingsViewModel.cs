// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DSView.Infrastructure;

namespace DSView.Desktop;

// UI thread only. The store receives an immutable snapshot and owns its IO.
internal sealed class AppearanceSettingsViewModel : ObservableObject
{
    private readonly IAppearanceStore _store;
    private DisplayPreferences _saved;
    private DisplayPreferences? _draft;
    private string? _message;

    public AppearanceSettingsViewModel(IAppearanceStore store)
    {
        _store = store;
        var loaded = store.Load(); _saved = loaded.Preferences; _message = loaded.Warning;
        OpenCommand = new RelayCommand(Open, () => !IsOpen && !IsSaving);
        CancelCommand = new RelayCommand(Cancel, () => IsOpen && !IsSaving);
        RestoreDefaultsCommand = new RelayCommand(() => SetDraft(DisplayPreferences.Default), () => IsOpen && !IsSaving);
        ApplyCommand = new AsyncRelayCommand(ApplyAsync, () => IsOpen && Current.IsValid);
        ApplyCommand.PropertyChanged += (_, _) => { OnPropertyChanged(nameof(IsSaving)); RefreshCommands(); };
    }
    public DisplayPreferences Current => _draft ?? _saved;
    public bool IsOpen => _draft != null;
    public bool IsSaving => ApplyCommand.IsRunning;
    public string? Message => _message;
    public double TextSize { get => Current.TextSize; set => SetDraft(Current with { TextSize = value }); }
    public double DataTextSize { get => Current.DataTextSize; set => SetDraft(Current with { DataTextSize = value }); }
    public double WaveformThickness { get => Current.WaveformThickness; set => SetDraft(Current with { WaveformThickness = value }); }
    public bool ReverseWheel { get => Current.ReverseWheel; set => SetDraft(Current with { ReverseWheel = value }); }
    public IRelayCommand OpenCommand { get; }
    public IRelayCommand CancelCommand { get; }
    public IRelayCommand RestoreDefaultsCommand { get; }
    public IAsyncRelayCommand ApplyCommand { get; }

    private void Open() { if (IsOpen || IsSaving) return; _draft = _saved; NotifyDraft(); }
    private void Cancel() { if (!IsOpen || IsSaving) return; _draft = null; NotifyDraft(); }
    private void SetDraft(DisplayPreferences value)
    {
        if (!IsOpen || IsSaving || !value.IsValid || value == _draft) return;
        _draft = value; NotifyDraft();
    }
    private async Task ApplyAsync()
    {
        if (!IsOpen || IsSaving) return;
        var snapshot = Current;
        AppearanceSaveResult result;
        try { result = await _store.SaveAsync(snapshot); }
        catch (Exception)
        {
            // Command boundary: an unexpected adapter fault must not crash the UI
            // or publish the draft. Report it here; no automatic retry or overwrite.
            result = new(false, "Unexpected settings error. The draft remains open; Retry or Cancel.");
        }
        _message = result.Error;
        if (result.Succeeded) { _saved = snapshot; _draft = null; }
        NotifyDraft();
    }
    private void NotifyDraft()
    {
        OnPropertyChanged(nameof(Current)); OnPropertyChanged(nameof(IsOpen));
        OnPropertyChanged(nameof(TextSize)); OnPropertyChanged(nameof(DataTextSize));
        OnPropertyChanged(nameof(WaveformThickness)); OnPropertyChanged(nameof(ReverseWheel));
        OnPropertyChanged(nameof(Message)); RefreshCommands();
    }
    private void RefreshCommands()
    {
        OpenCommand.NotifyCanExecuteChanged(); CancelCommand.NotifyCanExecuteChanged();
        RestoreDefaultsCommand.NotifyCanExecuteChanged(); ApplyCommand.NotifyCanExecuteChanged();
    }
}
