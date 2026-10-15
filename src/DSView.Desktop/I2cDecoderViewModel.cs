// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DSView.Domain;
using DSView.Infrastructure;

namespace DSView.Desktop;

internal sealed record DecoderChannel(int Id, string Label);

// UI-thread owner. The worker delegate performs blocking/native work off-thread.
internal sealed class I2cDecoderViewModel : ObservableObject, IDisposable
{
    private readonly Workbench model;
    private readonly Func<Capture, I2cChannelMap, CancellationToken, Task<I2cDecodeResult>> decode;
    private Capture? source;
    private DecoderChannel? scl, sda;
    private bool open, disposed, publishing;
    private int generation;
    private string status = "";
    private Capture? completedSource;
    public I2cDecoderViewModel(Workbench model,
        Func<Capture, I2cChannelMap, CancellationToken, Task<I2cDecodeResult>>? decode = null)
    {
        this.model = model;
        this.decode = decode ?? ((capture, channels, token) => Task.Run(() =>
            NativeI2cSource.DecodeMappedAsync(capture, channels,
                NativeI2cSource.ReadConfiguration(NativeI2cSource.ConfigurationPath), token), token));
        DecodeCommand = new AsyncRelayCommand(RunAsync, () => IsOpen && CanEdit && Problem.Length == 0);
        CancelCommand = new RelayCommand(() => { DecodeCommand.Cancel(); Status = "Stopping…"; }, () => IsRunning && !DecodeCommand.IsCancellationRequested);
        CloseCommand = new RelayCommand(Close);
        ShowResultCommand = new RelayCommand(() => { model.ShowDecoderSource("i2c"); Close(); }, () => completedSource != null && ReferenceEquals(completedSource, model.Capture) && !IsRunning);
        DecodeCommand.PropertyChanged += (_, _) => RefreshCommands();
        model.Changed += SourceChanged;
        SourceChanged();
    }
    public IReadOnlyList<DecoderChannel> Channels { get; private set; } = [];
    public DecoderChannel? Scl { get => scl; set { if (CanEdit && SetProperty(ref scl, value)) DraftChanged(); } }
    public DecoderChannel? Sda { get => sda; set { if (CanEdit && SetProperty(ref sda, value)) DraftChanged(); } }
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
        : scl == null || sda == null || !Channels.Contains(scl) || !Channels.Contains(sda) ? "Choose captured signals for SCL and SDA."
        : scl.Id == sda.Id ? "SCL and SDA must use different signals." : "";
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
    private void DraftChanged() { Status = ""; completedSource = null; RefreshCommands(); }
    private void SourceChanged()
    {
        if (publishing || disposed) return;
        if (!ReferenceEquals(source, model.Capture))
        {
            ++generation; DecodeCommand.Cancel(); source = model.Capture; completedSource = null;
            Channels = source?.Signals.OrderBy(s => s.Id).Select(s => new DecoderChannel(s.Id, $"D{s.Id} · {s.Name}")).ToArray() ?? [];
            var inputs = source?.EffectiveDecoderSources.FirstOrDefault(s => s.Id == "i2c")?.Inputs;
            scl = Channels.FirstOrDefault(c => c.Id == inputs?.FirstOrDefault(i => i.Role == "scl")?.Channel);
            sda = Channels.FirstOrDefault(c => c.Id == inputs?.FirstOrDefault(i => i.Role == "sda")?.Channel);
            Status = IsRunning ? "Source changed. Previous decode cancelled." : "";
            OnPropertyChanged(nameof(Channels)); OnPropertyChanged(nameof(Scl)); OnPropertyChanged(nameof(Sda));
        }
        RefreshCommands();
    }
    private void RefreshCommands()
    {
        OnPropertyChanged(nameof(IsRunning)); OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(Problem));
        OnPropertyChanged(nameof(HasProblem)); OnPropertyChanged(nameof(HasResult));
        DecodeCommand.NotifyCanExecuteChanged(); CancelCommand.NotifyCanExecuteChanged(); ShowResultCommand.NotifyCanExecuteChanged();
    }
    private async Task RunAsync(CancellationToken cancellation)
    {
        if (!IsOpen || Problem.Length != 0) return;
        var original = source!; var channels = new I2cChannelMap(scl!.Id, sda!.Id); int request = ++generation;
        completedSource = null; Status = "Decoding I²C… You can keep inspecting the waveform.";
        bool Current() => !disposed && request == generation && ReferenceEquals(source, original) && ReferenceEquals(model.Capture, original);
        try
        {
            var result = await decode(original, channels, cancellation);
            if (!Current()) return;
            cancellation.ThrowIfCancellationRequested();
            if (!ReferenceEquals(result.Source, original) || result.Channels != channels)
                throw new InvalidDataException("Decoder returned a different source or channel mapping.");
            publishing = true;
            bool applied;
            try { applied = model.ApplyI2cResult(result); }
            finally { publishing = false; }
            if (!applied) { Status = "Cursor edit in progress. Results kept unchanged; finish the edit and decode again."; return; }
            source = model.Capture; completedSource = source;
            Status = result.Packets.Count == 0 ? "No I²C packets found. Check SCL and SDA." : $"{result.Packets.Count} I²C packets decoded.";
        }
        catch (OperationCanceledException) { if (Current()) Status = "Cancelled. Previous results kept."; }
        catch (Exception ex) { if (Current()) Status = "Decode failed. Previous results kept. " + ex.Message; }
        finally { RefreshCommands(); }
    }
    public void Dispose() { if (disposed) return; Close(); disposed = true; model.Changed -= SourceChanged; }
}
