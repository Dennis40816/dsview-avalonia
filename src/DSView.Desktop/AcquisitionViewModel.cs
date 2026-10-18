// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DSView.Domain;
using DSView.Infrastructure;

namespace DSView.Desktop;

// UI-thread owner; preparation and acquisition execute off-thread. Only this owner
// may admit its pending capture into the shared workbench.
internal sealed class AcquisitionViewModel : ObservableObject, IAsyncDisposable
{
    private readonly Workbench model;
    private readonly Func<bool> available;
    private readonly Func<Task<bool>> confirmReplacement;
    private readonly Action<AcquisitionResult> published;
    private readonly Func<DemoCaptureSettings, IAcquisitionSource> create;
    private readonly Action<string>? decodeRequested;
    private readonly TimeProvider time;
    private long? lastPreviewPoll;
    private AcquisitionPreview? preview;
    private int sessionGeneration;
    private AcquisitionSession? session;
    private Task<AcquisitionResult>? sourceTask;
    private Capture? observed;
    private AcquisitionResult? pending;
    private int generation;
    private bool open, disposed, applying, stopping;
    private string status = "";
    private double progress;
    public AcquisitionViewModel(Workbench model, Func<bool> available, Func<Task<bool>> confirmReplacement,
        Action<AcquisitionResult> published, Func<DemoCaptureSettings, IAcquisitionSource>? create = null, Action<string>? decodeRequested = null,
        TimeProvider? time = null)
    {
        this.model = model; this.available = available; this.confirmReplacement = confirmReplacement;
        this.published = published; this.create = create ?? (settings => settings.CreateSource()); observed = model.Capture;
        this.decodeRequested = decodeRequested;
        this.time = time ?? TimeProvider.System;
        StartCommand = new AsyncRelayCommand(StartAsync, () => CanEdit && Problem.Length == 0 && available());
        StopCommand = new RelayCommand(() => { stopping = true; session?.RequestStop(); Status = "Stopping…"; Refresh(); }, () => IsAcquiring && !stopping);
        PrimaryCommand = new RelayCommand(() =>
        { if (IsAcquiring) StopCommand.Execute(null); else if (HasResult) IsOpen = true; else _ = StartCommand.ExecuteAsync(null); },
            () => IsAcquiring ? StopCommand.CanExecute(null) : HasResult ? !IsBusy : StartCommand.CanExecute(null));
        AcceptCommand = new AsyncRelayCommand(PublishAsync, () => HasResult && !IsBusy && available() && !model.HasPendingEdit);
        DiscardCommand = new RelayCommand(() => { pending = null; Status = "Current analysis kept."; Refresh(); }, () => HasResult && !IsBusy);
        CancelCommand = new AsyncRelayCommand(StopAsync, () => IsAcquiring);
        ToggleCommand = new RelayCommand(() => IsOpen = !IsOpen);
        CloseCommand = new RelayCommand(() => IsOpen = false);
        DecodeDemoCommand = new RelayCommand(() => { if (DemoDecoder is { } binding) decodeRequested?.Invoke(binding.Id); }, () => CanDecodeDemo && available());
        Draft.PropertyChanged += (_, _) => Refresh(); model.Changed += SourceChanged;
        StartCommand.PropertyChanged += (_, _) => Refresh(); AcceptCommand.PropertyChanged += (_, _) => Refresh();
    }
    public DemoCaptureDraft Draft { get; } = new();
    public IAsyncRelayCommand StartCommand { get; }
    public IRelayCommand StopCommand { get; }
    public IRelayCommand PrimaryCommand { get; }
    public IAsyncRelayCommand AcceptCommand { get; }
    public IRelayCommand DiscardCommand { get; }
    public IAsyncRelayCommand CancelCommand { get; }
    public IRelayCommand ToggleCommand { get; }
    public IRelayCommand CloseCommand { get; }
    public IRelayCommand DecodeDemoCommand { get; }
    private DecoderSource? DemoDecoder => model.Capture?.EffectiveDecoderSources.FirstOrDefault(s =>
        s.Id == "demo-i2c" && s.EffectiveProtocol == "i2c" || s.Id == "demo-uart" && s.EffectiveProtocol == "uart"
        || s.Id == "demo-spi" && s.EffectiveProtocol == "spi");
    public bool CanDecodeDemo => decodeRequested != null && !IsBusy && !HasResult && DemoDecoder != null;
    public string DecodeLabel => DemoDecoder?.EffectiveProtocol switch { "uart" => "Decode UART", "spi" => "Decode SPI", _ => "Decode I²C" };
    public bool IsOpen
    {
        get => open;
        set { if (SetProperty(ref open, value) && !value) Preview = null; }
    }
    public AcquisitionPreview? Preview
    {
        get => preview;
        private set
        {
            if (ReferenceEquals(preview, value)) return;
            preview = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasPreview));
            OnPropertyChanged(nameof(PreviewRange)); OnPropertyChanged(nameof(PreviewReduced));
        }
    }
    public bool HasPreview => Preview != null;
    public bool PreviewReduced => Preview?.WindowReduced == true;
    public string PreviewRange => Preview is { } p
        ? $"{TimingText.Duration(p.StartSample / (double)p.SampleRate)} – {TimingText.Duration(p.EndSample / (double)p.SampleRate)}" : "";
    public bool ShowConfiguration => !IsAcquiring;
    public bool IsAcquiring => session != null && sourceTask?.IsCompleted != true;
    public bool IsBusy => StartCommand.IsRunning || AcceptCommand.IsRunning;
    public bool CanEdit => !disposed && !IsBusy && pending == null;
    public bool HasResult => pending != null;
    public bool HasStatus => Status.Length != 0;
    public string Status { get => status; private set { SetProperty(ref status, value); OnPropertyChanged(nameof(HasStatus)); } }
    public string PrimaryLabel => IsAcquiring ? "Stop" : HasResult ? "Review" : "Start";
    public bool ShowStartIcon => !IsAcquiring && !HasResult;
    public string ResultLabel => pending?.RequiresPartialAcceptance == true ? "Use partial capture" : "Show capture";
    public string Problem => Draft.TryCreate(out _);
    public bool HasProblem => Problem.Length != 0;
    public double Progress { get => progress; private set => SetProperty(ref progress, value); }
    public void Poll()
    {
        if (session is { } current)
        {
            var value = current.Progress; Progress = value.RetainedSamples * 100d / current.Plan.SampleLimit;
            if (!stopping) Status = value.Phase == AcquisitionPhase.Preparing ? "Preparing Demo…" : $"Acquiring Demo… {Progress:0}% · Current capture stays visible.";
            if (!disposed && IsOpen && IsAcquiring && sessionGeneration == generation)
            {
                long now = time.GetTimestamp();
                if (lastPreviewPoll == null || time.GetElapsedTime(lastPreviewPoll.Value, now) >= TimeSpan.FromMilliseconds(100))
                {
                    lastPreviewPoll = now;
                    if (current.LatestPreview is { } next && next.Operation == current.Operation) Preview = next;
                    if (!stopping) current.RequestPreview();
                }
            }
            else Preview = null;
        }
        Refresh();
    }
    private void Refresh()
    {
        foreach (string property in new[] { nameof(IsAcquiring), nameof(IsBusy), nameof(CanEdit), nameof(HasResult), nameof(PrimaryLabel), nameof(ShowStartIcon), nameof(ResultLabel), nameof(Problem), nameof(HasProblem), nameof(CanDecodeDemo), nameof(ShowConfiguration) }) OnPropertyChanged(property);
        StartCommand.NotifyCanExecuteChanged(); StopCommand.NotifyCanExecuteChanged(); PrimaryCommand.NotifyCanExecuteChanged();
        AcceptCommand.NotifyCanExecuteChanged(); DiscardCommand.NotifyCanExecuteChanged(); CancelCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(DecodeLabel)); DecodeDemoCommand.NotifyCanExecuteChanged();
    }
    private void SourceChanged()
    {
        if (applying) return;
        if (!ReferenceEquals(observed, model.Capture))
        {
            observed = model.Capture; ++generation; session?.Cancel(); pending = null; Preview = null;
            Status = "";
        }
        Refresh();
    }
    private async Task StartAsync()
    {
        if (disposed || session != null || pending != null || !available() || Draft.TryCreate(out var settings).Length != 0) return;
        int request = ++generation; observed = model.Capture; stopping = false; Progress = 0; Status = "Preparing Demo…";
        sessionGeneration = request; Preview = null; lastPreviewPoll = null; IsOpen = true;
        try
        {
            await using (var current = new AcquisitionSession(create(settings!)))
            {
                session = current; sourceTask = current.RunAsync(); Poll();
                var result = await sourceTask;
                if (disposed || request != generation || result.Operation != current.Operation) return;
                if (result.IsWorkbenchCompatible) pending = result with { RawCapture = settings!.Configure(result.RawCapture!) };
                Status = result.Termination switch
                {
                    AcquisitionTermination.Completed => result.IsWorkbenchCompatible ? "Capture ready." : "Too few samples to display. Current analysis kept.",
                    AcquisitionTermination.Cancelled => "Acquisition discarded. Current analysis kept.",
                    AcquisitionTermination.Stopped => result.IsWorkbenchCompatible ? $"Stopped · {result.RetainedSamples:N0} samples retained." : "Stopped before enough samples arrived. Current analysis kept.",
                    _ => $"{result.Termination} · {result.RetainedSamples:N0} samples retained. {result.Detail}"
                };
            }
        }
        catch (Exception ex) { if (request == generation) Status = "Acquisition failed. Current analysis kept. " + ex.Message; }
        finally { session = null; sourceTask = null; Preview = null; Refresh(); }
        if (request != generation || disposed) return;
        if (pending?.CanPublish == true) await PublishAsync();
        else if (pending != null) IsOpen = true;
    }
    private async Task PublishAsync()
    {
        var result = pending; int request = generation; var original = model.Capture;
        bool Current() => !disposed && request == generation && ReferenceEquals(result, pending) && ReferenceEquals(original, model.Capture);
        if (result?.IsWorkbenchCompatible != true) return;
        if (!available() || model.HasPendingEdit) { Status = "Capture ready. Finish the current operation to show it."; IsOpen = true; return; }
        try
        {
            if (!await confirmReplacement()) { if (Current()) { Status = "Capture ready. Current analysis kept."; IsOpen = true; } return; }
            if (!Current() || !available() || model.HasPendingEdit) return;
            string priorState = AnalysisFile.StateKey(model.SnapshotState(new(null, 0), null));
            var groups = model.Groups.Where(g => g.Name != "All signals").ToArray(); var colors = new Dictionary<int,string>(model.Colors);
            var prepared = await Task.Run(() => Workbench.PrepareValidatedAnalysis(Workbench.CreateImportedAnalysis(
                new("Demo · " + result.Plan.SourceName, result.RawCapture!), groups, colors)));
            if (!Current() || !available() || model.HasPendingEdit) return;
            if (priorState != AnalysisFile.StateKey(model.SnapshotState(new(null, 0), null)))
            { Status = "View changed. Capture ready when you are."; IsOpen = true; return; }
            applying = true;
            try { model.ApplyAnalysis(prepared); observed = model.Capture; published(result); }
            finally { applying = false; }
            pending = null; Status = result.Termination == AcquisitionTermination.Completed ? "Demo capture complete." : "Partial Demo capture shown.";
        }
        catch (Exception ex) { if (Current()) Status = "Capture could not be shown. Current analysis kept. " + ex.Message; }
        finally { Refresh(); }
    }
    public async Task StopAsync()
    {
        ++generation; pending = null; session?.Cancel(); Preview = null;
        var running = StartCommand.ExecutionTask; var accepting = AcceptCommand.ExecutionTask;
        if (running != null) await running;
        if (accepting != null) await accepting;
        Status = "Acquisition discarded. Current analysis kept."; Refresh();
    }
    public async ValueTask DisposeAsync()
    {
        if (disposed) return; disposed = true; model.Changed -= SourceChanged;
        await StopAsync();
    }
}
