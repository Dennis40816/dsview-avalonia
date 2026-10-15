// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DSView.Domain;
using DSView.Infrastructure;

namespace DSView.Desktop;

internal sealed record DecoderTarget(DecoderSource? Source, string Label);

// UI-thread owner. The worker delegate performs blocking/native work off-thread.
internal sealed class DecoderViewModel : ObservableObject, IDisposable
{
    private readonly Workbench model;
    private readonly Func<Capture, DecoderSource, CancellationToken, Task<IDecoderResult>> decode;
    private Capture? source;
    private DecoderTarget? target;
    private string? completedId;
    public DecoderDraft Draft { get; private set; } = null!;
    private bool open, disposed, publishing;
    private int generation;
    private string status = "";
    private string draftProblem = "";
    private bool observedPendingEdit;
    private Capture? completedSource;
    public DecoderViewModel(Workbench model,
        Func<Capture, DecoderSource, CancellationToken, Task<IDecoderResult>>? decode = null)
    {
        this.model = model;
        this.decode = decode ?? RunNativeAsync;
        DecodeCommand = new AsyncRelayCommand(RunAsync, () => IsOpen && CanEdit && Problem.Length == 0);
        CancelCommand = new RelayCommand(() => { DecodeCommand.Cancel(); Status = "Stopping…"; }, () => IsRunning && !DecodeCommand.IsCancellationRequested);
        CloseCommand = new RelayCommand(Close);
        ShowResultCommand = new RelayCommand(() => { model.ShowDecoderSource(completedId!); Close(); }, () => completedSource != null && ReferenceEquals(completedSource, model.Capture) && !IsRunning);
        DecodeCommand.PropertyChanged += (_, _) => RefreshCommands();
        model.Changed += SourceChanged;
        SourceChanged();
    }
    public IReadOnlyList<DecoderTarget> Targets { get; private set; } = [];
    public DecoderTarget? Target
    {
        get => target;
        set { if (CanEdit && value != null && Targets.Contains(value) && SetProperty(ref target, value)) { SetDraft(value.Source); DraftChanged(); } }
    }
    public bool IsOpen => open && !disposed;
    public bool IsRunning => DecodeCommand.IsRunning;
    public bool CanEdit => !IsRunning && !disposed;
    public string Status { get => status; private set { if (SetProperty(ref status, value)) OnPropertyChanged(nameof(HasStatus)); } }
    public bool HasStatus => Status.Length != 0;
    public bool HasProblem => Problem.Length != 0;
    public bool HasResult => completedSource != null && ReferenceEquals(completedSource, model.Capture) && !IsRunning;
    public string Problem => source == null ? "Open a capture first."
        : source.Gaps.Count > 0 || source.SampleCount is < 10 or > 64_000_000 ? "This decoder needs 10 to 64,000,000 continuous samples without gaps."
        : model.HasPendingEdit ? "Finish or cancel the cursor edit first."
        : draftProblem;
    public IAsyncRelayCommand DecodeCommand { get; }
    public IRelayCommand CancelCommand { get; }
    public IRelayCommand CloseCommand { get; }
    public IRelayCommand ShowResultCommand { get; }
    public void Open() { if (disposed) return; SourceChanged(); open = true; OnPropertyChanged(nameof(IsOpen)); RefreshCommands(); }
    public void Close()
    {
        if (IsRunning) Status = "Cancellation requested. Previous results kept.";
        ++generation; DecodeCommand.Cancel(); open = false;
        OnPropertyChanged(nameof(IsOpen)); RefreshCommands();
    }
    public async Task StopAsync()
    {
        Close();
        // Window shutdown must let the native adapter confirm worker exit.
        if (DecodeCommand.ExecutionTask is { } pending) await pending;
    }
    private void DraftChanged()
    {
        Draft.TryCreate(out _, out draftProblem);
        Status = ""; completedSource = null; RefreshCommands();
    }
    private void SourceChanged()
    {
        if (publishing || disposed) return;
        bool changed = false;
        if (Draft == null || !ReferenceEquals(source, model.Capture))
        {
            changed = true;
            ++generation; DecodeCommand.Cancel(); source = model.Capture; completedSource = null;
            RefreshTargets(null);
            Status = IsRunning ? "Source changed. Previous decode cancelled." : "";
        }
        // Waveform hover/viewport notifications must not rebuild a settings
        // request or refresh hidden controls on every pointer movement.
        if (changed || observedPendingEdit != model.HasPendingEdit)
        { observedPendingEdit = model.HasPendingEdit; RefreshCommands(); }
    }
    private void SetDraft(DecoderSource? saved)
    {
        if (Draft != null) Draft.Changed -= DraftChanged;
        Draft = new(source, saved, () => CanEdit);
        Draft.Changed += DraftChanged;
        Draft.TryCreate(out _, out draftProblem);
        OnPropertyChanged(nameof(Draft));
    }
    private void RefreshTargets(string? selectedId)
    {
        Targets = new[] { new DecoderTarget(null, "New decoder") }.Concat(
            (source?.EffectiveDecoderSources ?? []).Where(s => source!.DecoderSources != null || source.Packets.Any(p => p.Decoder == s.Id))
                .Select(s => new DecoderTarget(s, s.DisplayName))).ToArray();
        target = Targets.FirstOrDefault(t => t.Source?.Id == selectedId) ?? Targets[0];
        SetDraft(target.Source);
        OnPropertyChanged(nameof(Targets)); OnPropertyChanged(nameof(Target));
    }
    private static Task<IDecoderResult> RunNativeAsync(Capture capture, DecoderSource binding, CancellationToken token) =>
        Task.Run<IDecoderResult>(async () => binding.EffectiveProtocol == "i2c"
            ? await NativeI2cSource.DecodeMappedAsync(capture,
                new(binding.Inputs.Single(i => i.Role == "scl").Channel, binding.Inputs.Single(i => i.Role == "sda").Channel),
                NativeI2cSource.ReadConfiguration(NativeI2cSource.ConfigurationPath), token, binding.Id)
            : await NativeSerialSource.DecodeAsync(capture, binding,
                NativeSerialSource.ReadConfiguration(NativeSerialSource.ConfigurationPath), token), token);
    private void RefreshCommands()
    {
        OnPropertyChanged(nameof(IsRunning)); OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(Problem));
        OnPropertyChanged(nameof(HasProblem)); OnPropertyChanged(nameof(HasResult));
        DecodeCommand.NotifyCanExecuteChanged(); CancelCommand.NotifyCanExecuteChanged(); ShowResultCommand.NotifyCanExecuteChanged();
    }
    private async Task RunAsync(CancellationToken cancellation)
    {
        if (!IsOpen || Problem.Length != 0) return;
        if (!Draft.TryCreate(out var binding, out _)) return;
        var original = source!; string label = Draft.Protocol; int request = ++generation;
        completedSource = null; Status = $"Decoding {label}… You can keep inspecting the waveform.";
        bool Current() => !disposed && request == generation && ReferenceEquals(source, original) && ReferenceEquals(model.Capture, original);
        try
        {
            var result = await decode(original, binding!, cancellation);
            if (!Current()) return;
            cancellation.ThrowIfCancellationRequested();
            DecoderSource.Validate([result.Binding]);
            if (!ReferenceEquals(result.Source, original) || !result.Binding.SameConfiguration(binding!))
                throw new InvalidDataException("Decoder returned a different source or settings.");
            publishing = true;
            bool applied;
            try { applied = model.ApplyDecoderResult(result); }
            finally { publishing = false; }
            if (!applied) { Status = "Cursor edit in progress. Results kept unchanged; finish the edit and decode again."; return; }
            source = model.Capture; completedSource = source; completedId = binding!.Id;
            RefreshTargets(completedId);
            Status = result.Packets.Count == 0 ? "No packets found. Check signals and settings." : $"{result.Packets.Count} {label} packets decoded.";
        }
        catch (OperationCanceledException) { if (Current()) Status = "Cancelled. Previous results kept."; }
        catch (Exception ex) { if (Current()) Status = "Decode failed. Previous results kept. " + ex.Message; }
        finally { RefreshCommands(); }
    }
    public void Dispose() { if (disposed) return; Close(); disposed = true; model.Changed -= SourceChanged; Draft.Changed -= DraftChanged; }
}
