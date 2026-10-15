// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DSView.Domain;

namespace DSView.Desktop;

// A local editing draft. Capture data and physical probe identity never change.
internal sealed class ChannelAppearanceViewModel : ObservableObject
{
    private readonly Workbench model;
    private readonly Capture capture;
    private readonly int channel;
    private string? draft;
    public ChannelAppearanceViewModel(Workbench model, Signal signal)
    {
        this.model = model; capture = model.Capture!; channel = signal.Id;
        Title = $"D{channel} · {signal.Name}";
        ProbeBrush = Brush.Parse(ChannelPalette.Probe(channel));
        string saved = model.Colors.GetValueOrDefault(channel, ChannelPalette.DefaultWaveform(channel));
        draft = Color.TryParse(saved, out var color) && color.A == 255 ? $"#{color.R:X2}{color.G:X2}{color.B:X2}" : saved;
        ApplyCommand = new RelayCommand(Apply, () => IsOpen && IsCurrent && ValidColor);
        CancelCommand = new RelayCommand(Cancel);
        DefaultCommand = new RelayCommand(() => ColorText = ChannelPalette.DefaultWaveform(channel));
        SelectColorCommand = new RelayCommand<string>(value => { if (value != null) ColorText = value; });
    }
    public string Title { get; }
    public IBrush ProbeBrush { get; }
    public bool IsOpen => draft != null;
    private bool IsCurrent => ReferenceEquals(capture, model.Capture) && capture.Signals.Any(s => s.Id == channel);
    private bool ValidColor => draft is { Length: 7 } && draft[0] == '#' && draft.AsSpan(1).ToArray().All(Uri.IsHexDigit);
    public string? Error => ValidColor ? null : "Use six hex digits, for example #D4AD00.";
    public bool HasError => Error != null;
    public IBrush PreviewBrush => ValidColor ? Brush.Parse(draft!) : Brushes.Transparent;
    public string ColorText
    {
        get => draft ?? "";
        set
        {
            if (!IsOpen || draft == value) return;
            draft = value;
            OnPropertyChanged(); OnPropertyChanged(nameof(Error)); OnPropertyChanged(nameof(HasError)); OnPropertyChanged(nameof(PreviewBrush));
            ApplyCommand.NotifyCanExecuteChanged();
        }
    }
    public IRelayCommand ApplyCommand { get; }
    public IRelayCommand CancelCommand { get; }
    public IRelayCommand DefaultCommand { get; }
    public IRelayCommand<string> SelectColorCommand { get; }
    public event Action? Applied;
    public event Action? DismissRequested;
    private void Apply()
    {
        if (!IsOpen || !IsCurrent || !ValidColor) return;
        string chosen = draft!.ToUpperInvariant();
        if (chosen == ChannelPalette.DefaultWaveform(channel)) model.Colors.Remove(channel);
        else model.Colors[channel] = chosen;
        draft = null; model.Notify(); Applied?.Invoke(); DismissRequested?.Invoke();
    }
    public void Cancel() { if (!IsOpen) return; draft = null; DismissRequested?.Invoke(); }
}
