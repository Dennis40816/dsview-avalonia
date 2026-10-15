// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using DSView.Domain;
using DSView.Infrastructure;

namespace DSView.Desktop;

public sealed partial class Workbench : ObservableObject
{
    public Capture? Capture { get; private set; }
    public ObservableCollection<ObservationGroup> Groups { get; } = [new("I²C + Control", [0, 1, 8], ["i2c"]), new("UART + SPI", [2, 3, 4, 5, 6, 7], ["spi", "ua", "ub"]), new("All signals", Enumerable.Range(0, 32).ToArray(), [])];
    public ObservationGroup Group { get; private set; }
    public Dictionary<int, string> Colors { get; } = [];
    public double Start { get; private set; } = 24_000;
    public double End { get; private set; } = 33_500;
    public Endpoint? A { get; private set; }
    public Endpoint? B { get; private set; }
    public LocalMeasurement? PinnedMeasurement { get; private set; }
    public Endpoint? Probe { get; private set; }
    public int Mode { get; private set; }
    public int? Armed { get; private set; }
    public Packet? Selected { get; private set; }
    public CaptureIssue? SelectedIssue { get; private set; }
    public int? SelectedProtocolIndex { get; private set; }
    public ProtocolItem? SelectedProtocol { get; private set; }
    public IReadOnlyList<CaptureIssue> Issues { get; private set; } = [];
    public event Action<int>? SignalFocusRequested;
    public event Action<IReadOnlyList<int>>? MeasurementFocusRequested;
    public bool Details { get; private set; }
    public bool PacketsTab { get; private set; }
    public string Hint { get; private set; } = "";
    private (Endpoint? A, Endpoint? B, int Mode, LocalMeasurement? Measurement)? editOrigin;
    private bool selectingPair;
    private bool draggingEndpoint;
    private ObservationGroup? cachedGroup;
    private Capture? cachedCapture;
    private Signal[] cachedSignals = [];
    private Decoder[] cachedDecoders = [];
    private Decoder[] cachedSources = [];
    public string Query { get; private set; } = "";
    private readonly Stack<(double Start, double End, ObservationGroup Group)> history = new();
    public event Action? Changed;
    public Workbench() => Group = Groups[0];
    public bool HasPendingEdit => editOrigin != null || Armed != null || draggingEndpoint;
    public AnalysisState SnapshotState(ViewAnchor vertical, int? edgeChannel)
    {
        var committed = editOrigin ?? (A, B, Mode, PinnedMeasurement);
        return new(Groups.Where(g => g.Name != "All signals").Select(g => new ObservationGroup(g.Name, g.Channels.ToArray(), g.Decoders.ToArray())).ToArray(),
            Group.Name, Colors.OrderBy(c => c.Key).ToDictionary(), Start, End, committed.Item1, committed.Item2, committed.Item3, committed.Item4 != null,
            Selected?.Id, SelectedIssue?.Diagnostic.Id, Details && Selected != null, PacketsTab, Query, vertical, edgeChannel) { SelectedProtocolIndex = SelectedProtocolIndex, Measurements = keptMeasurements.ToArray() };
    }
    public void RestoreAnalysis(AnalysisDocument document)
    {
        AnalysisFile.Validate(document);
        ApplyAnalysis(PrepareValidatedAnalysis(document));
    }
    public static AnalysisDocument CreateImportedAnalysis(DslImportResult source, ObservationGroup[] groups, Dictionary<int, string> colors,
        CancellationToken cancellation = default)
    {
        var state = new AnalysisState(groups.Select(g => new ObservationGroup(g.Name, g.Channels.ToArray(), g.Decoders.ToArray())).ToArray(),
            "All signals", new(colors), 0, source.Capture.SampleCount, null, null, 0, false, null, null, false, false, "",
            new(null, 0), source.Capture.Signals.FirstOrDefault()?.Id) { Measurements = [] };
        var document = new AnalysisDocument(AnalysisDocument.FileFormat, AnalysisDocument.CurrentVersion, source.SourceName, source.Capture, state);
        AnalysisFile.Validate(document, cancellation); return document;
    }
    internal sealed record PreparedAnalysis(AnalysisDocument Document, IReadOnlyList<CaptureIssue> Issues,
        Packet? Selected, CaptureIssue? Issue, ProtocolItem? Protocol, LocalMeasurement? Local, ObservationGroup[] Groups);
    // Used only after AnalysisFile.ReadAsync has validated the entire file on a worker.
    internal static PreparedAnalysis PrepareValidatedAnalysis(AnalysisDocument document)
    {
        var state = document.State; var capture = document.Capture; var issues = capture.Issues;
        var selected = capture.Packets.FirstOrDefault(p => p.Id == state.SelectedPacket);
        var issue = issues.FirstOrDefault(i => ReferenceEquals(i.Packet, selected) && i.Diagnostic.Id == state.SelectedIssue);
        var local = state.LocalPulse ? capture.MeasureAt(state.A!.Channel, state.A.Sample) : null;
        var groups = state.Groups.Select(g => new ObservationGroup(g.Name, g.Channels.ToArray(), g.Decoders.ToArray()))
            .Append(new ObservationGroup("All signals", Enumerable.Range(0, 32).ToArray(), [])).ToArray();
        var protocol = selected != null && state.SelectedProtocolIndex is int index ? capture.ProtocolItems(selected)[index] : null;
        return new(document, issues, selected, issue, protocol, local, groups);
    }
    internal void ApplyAnalysis(PreparedAnalysis prepared)
    {
        var state = prepared.Document.State; var capture = prepared.Document.Capture;
        var groups = prepared.Groups;
        Capture = capture; Groups.Clear(); foreach (var group in groups) Groups.Add(group);
        Group = Groups.Single(g => g.Name == state.ActiveGroup);
        Colors.Clear(); foreach (var pair in state.Colors) Colors.Add(pair.Key, pair.Value);
        keptMeasurements.Clear(); keptMeasurements.AddRange(state.Measurements ?? []); removedMeasurement = null;
        A = state.A; B = state.B; Mode = state.Mode; PinnedMeasurement = prepared.Local;
        Selected = prepared.Selected; Issues = prepared.Issues; SelectedIssue = prepared.Issue; Details = state.Details;
        SelectedProtocolIndex = state.SelectedProtocolIndex;
        SelectedProtocol = prepared.Protocol;
        PacketsTab = state.PacketsTab; Query = state.Query; Start = state.Start; End = state.End;
        Probe = null; Armed = null; editOrigin = null; selectingPair = draggingEndpoint = false; Hint = ""; history.Clear();
        Notify();
    }
    private DecoderLane[] cachedLanes = [];
    private void CacheScope()
    {
        if (ReferenceEquals(cachedCapture, Capture) && ReferenceEquals(cachedGroup, Group)) return;
        if (!ReferenceEquals(cachedCapture, Capture)) cachedSources = Capture?.ResolvedDecoders.ToArray() ?? [];
        cachedCapture = Capture; cachedGroup = Group;
        cachedSignals = Capture?.Signals.Where(s => Group.Channels.Contains(s.Id)).ToArray() ?? [];
        cachedDecoders = Capture?.Packets.Count > 0
            ? cachedSources.Where(d => (IsAllSignals || Group.Decoders.Contains(d.Id))
                && Capture.Packets.Any(p => p.Decoder == d.Id) && d.Channels.All(id => cachedSignals.Any(s => s.Id == id))).ToArray()
            : [];
        cachedLanes = Capture == null ? [] : DecoderLane.Create(Capture, cachedDecoders);
    }
    public IReadOnlyList<Signal> Signals { get { CacheScope(); return cachedSignals; } }
    public IReadOnlyList<Decoder> Decoders { get { CacheScope(); return cachedDecoders; } }
    public IReadOnlyList<DecoderLane> DecoderLanes { get { CacheScope(); return cachedLanes; } }
    private IReadOnlyList<Decoder> SourceDecoders { get { CacheScope(); return cachedSources; } }
    public bool IsAllSignals => Group.Name == "All signals";
    private bool InPacketScope(Packet packet) => IsAllSignals || Group.Decoders.Contains(packet.Decoder)
        && SourceDecoders.Any(d => d.Id == packet.Decoder && d.Channels.All(Group.Channels.Contains));
    private bool MatchesSearch(Packet packet) => string.IsNullOrWhiteSpace(Query) || $"{packet.Id} {packet.Hex} {packet.Address} {packet.Title} {packet.Error} 0x{packet.Value:X}".Contains(Query, StringComparison.OrdinalIgnoreCase);
    public IEnumerable<Packet> Packets => (Capture?.Packets ?? []).Where(p => InPacketScope(p) && MatchesSearch(p));
    public PacketView PacketView()
    {
        if (Capture == null) return new([], PacketEmptyReason.NoCapture);
        if (Capture.Packets.Count == 0) return new([], PacketEmptyReason.NoRecords);
        var scoped = Capture.Packets.Where(InPacketScope).ToArray();
        if (scoped.Length == 0) return new([], PacketEmptyReason.Group);
        var matches = scoped.Where(MatchesSearch).ToArray();
        return new(matches, matches.Length == 0 ? PacketEmptyReason.Search : PacketEmptyReason.None);
    }
    public bool SelectionInGroup => Selected == null || InPacketScope(Selected);
    public string PacketSelectionContext => Selected == null ? "" : !SelectionInGroup ? "Selected packet is outside this Group."
        : PacketsTab && !MatchesSearch(Selected) ? "Selected packet is outside this search."
        : !PacketsTab && ((SelectedProtocol?.Span ?? Selected.Span).End < Start || (SelectedProtocol?.Span ?? Selected.Span).Start > End) ? "Source is outside the time view." : "";
    public double Us(double sample) => sample / (Capture?.SampleRate ?? 50_000_000) * 1_000_000;
    public string RangeText => TimeAxis.FormatRange(Start, End, Capture?.SampleRate ?? 50_000_000);
    public void Load(Capture capture)
    {
        if (capture.SampleRate <= 0 || capture.SampleCount < 10) throw new ArgumentException("Capture needs a valid sample rate and at least 10 samples.");
        DecoderSource.Validate(capture.EffectiveDecoderSources);
        keptMeasurements.Clear(); removedMeasurement = null;
        Capture = capture; A = B = Probe = null; PinnedMeasurement = null; Selected = null; SelectedIssue = null; Issues = capture.Issues; Details = false;
        SelectedProtocol = null; SelectedProtocolIndex = null;
        if (!capture.Signals.Any(s => Group.Channels.Contains(s.Id)))
            Group = Groups.FirstOrDefault(g => capture.Signals.Any(s => g.Channels.Contains(s.Id))) ?? Groups.Last();
        editOrigin = null; selectingPair = draggingEndpoint = false; Armed = null; Mode = 0; Hint = ""; Query = ""; PacketsTab = false; history.Clear();
        SetRange(0, capture.SampleCount);
    }
    public void Notify() { OnPropertyChanged(string.Empty); Changed?.Invoke(); }
    public bool ApplyI2cResult(I2cDecodeResult result) => ApplyDecoderResult(result);
    public bool ApplyDecoderResult(IDecoderResult result)
    {
        if (!ReferenceEquals(Capture, result.Source) || HasPendingEdit) return false;
        var decoded = result.ToCapture();
        Capture = decoded; Issues = decoded.Issues;
        // A packet ordinal or byte selection from the old decode is not evidence
        // of the same selection in the replacement result, even if IDs match.
        if (Selected != null && !decoded.Packets.Any(p => ReferenceEquals(p, Selected)))
        { Selected = null; SelectedIssue = null; SelectedProtocol = null; SelectedProtocolIndex = null; Details = false; }
        else if (SelectedIssue is { } priorIssue)
            SelectedIssue = Issues.FirstOrDefault(i => ReferenceEquals(i.Packet, priorIssue.Packet) && ReferenceEquals(i.Diagnostic, priorIssue.Diagnostic));
        Notify(); return true;
    }
    public void SetGroup(ObservationGroup group) { Group = group; if (Probe != null && !group.Channels.Contains(Probe.Channel)) Probe = null; if (!SelectionInGroup) Details = false; Notify(); }
    public void ShowAll() { PacketsTab = false; SetGroup(Groups.First(g => g.Name == "All signals")); }
    public void ShowDecoderSource(string decoderId) { if (Capture == null) return; PacketsTab = false; SetGroup(SourceGroup(decoderId)); }
    public void ShowAllPackets() { Query = ""; PacketsTab = true; SetGroup(Groups.First(g => g.Name == "All signals")); }
    public void Select(Packet packet) { Selected = packet; SelectedIssue = null; SelectedProtocol = null; SelectedProtocolIndex = null; Details = true; Notify(); }
    public void SelectProtocol(Packet packet, int index)
    {
        if (Capture == null || !Capture.Packets.Any(p => ReferenceEquals(p, packet)) || index < 0
            || packet.Annotations == null && !packet.HasI2cCells && !packet.IsSerial) return;
        var items = Capture.ProtocolItems(packet);
        if (index >= items.Count) return;
        Selected = packet; SelectedIssue = null; SelectedProtocolIndex = index; SelectedProtocol = items[index]; Details = true; Notify();
    }
    public void ShowSignal(int channel)
    {
        if (Capture?.Signals.Any(s => s.Id == channel) != true) return;
        Remember();
        if (!Group.Channels.Contains(channel)) Group = Groups.FirstOrDefault(g => g.Channels.Contains(channel)) ?? Group;
        PacketsTab = false; if (!SelectionInGroup) Details = false;
        Notify(); SignalFocusRequested?.Invoke(channel);
    }
    public void LocateIssue(CaptureIssue issue)
    {
        if (!Issues.Any(i => ReferenceEquals(i, issue))) return;
        Remember(); Selected = issue.Packet; SelectedIssue = issue; Details = true; PacketsTab = false;
        SelectedProtocol = null; SelectedProtocolIndex = null;
        Group = SourceGroup(issue.Packet.Decoder);
        var spans = issue.Diagnostic.Spans.Count > 0 ? issue.Diagnostic.Spans : [issue.Packet.Span];
        long first = spans.Min(s => s.Start), last = spans.Max(s => s.End);
        double pad = Math.Max(10, (last - first) * .2);
        SetRange(first - pad, last + pad);
        var source = SourceDecoders.FirstOrDefault(d => d.Id == issue.Packet.Decoder)?.Channels.FirstOrDefault();
        if (source is int channel) SignalFocusRequested?.Invoke(channel);
    }
    public void SelectIssue(CaptureIssue issue)
    {
        if (!Issues.Any(i => ReferenceEquals(i, issue))) return;
        Selected = issue.Packet; SelectedIssue = issue; SelectedProtocol = null; SelectedProtocolIndex = null; Details = true; Notify();
    }
    public void ToggleDetails() { Details = !Details; Notify(); }
    public void SetTab(bool packets) { PacketsTab = packets; Notify(); }
    public void Search(string value) { Query = value; Notify(); }
    public void Remember() { history.Push((Start, End, Group)); if (history.Count > 100) { var keep = history.Take(50).Reverse().ToArray(); history.Clear(); foreach (var item in keep) history.Push(item); } }
    public void Previous() { if (history.TryPop(out var prior)) { Group = Groups.Contains(prior.Group) ? prior.Group : Group; SetRange(prior.Start, prior.End); } }
    public void SetRange(double start, double end)
    {
        double total = Capture?.SampleCount ?? 50_000, span = Math.Clamp(end - start, 10, total);
        Start = Math.Clamp(start, 0, total - span); End = Start + span; Notify();
    }
    public void Zoom(double factor, double anchor = .5) { Remember(); var sample = Start + anchor * (End - Start); var span = (End - Start) * factor; SetRange(sample - anchor * span, sample + (1 - anchor) * span); }
    public void FitTime() { Remember(); SetRange(0, Capture?.SampleCount ?? 50_000); }
    public void Locate()
    {
        if (Selected == null) return;
        Remember(); Group = SourceGroup(Selected.Decoder);
        PacketsTab = false; var source = SelectedProtocol?.Span ?? Selected.Span;
        double pad = Math.Max(10, (source.End - source.Start) * (SelectedProtocol == null ? .08 : .2));
        SetRange(source.Start - pad, source.End + pad);
        var channel = SourceDecoders.FirstOrDefault(d => d.Id == Selected.Decoder)?.Channels.FirstOrDefault();
        if (channel is int id) SignalFocusRequested?.Invoke(id);
    }
    private ObservationGroup SourceGroup(string decoderId)
    {
        var decoder = SourceDecoders.FirstOrDefault(d => d.Id == decoderId);
        var existing = Groups.FirstOrDefault(g => g.Decoders.Contains(decoderId) && (decoder == null || decoder.Channels.All(g.Channels.Contains)));
        if (existing != null) return existing;
        if (decoder == null) return Groups.First(g => g.Name == "All signals");
        // A user may have removed the source decoder from every saved Group. Explicit Locate must still reveal it.
        string name = decoder.Name + " source"; int suffix = 2;
        while (Groups.Any(g => g.Name == name)) name = decoder.Name + " source " + suffix++;
        var source = new ObservationGroup(name, decoder.Channels, [decoderId]); Groups.Insert(Math.Max(0, Groups.Count - 1), source); return source;
    }
    private void BeginEdit() { editOrigin ??= (A, B, Mode, PinnedMeasurement); }
    internal bool IsChoosingPair => selectingPair && Armed == 0;
    public void SetMode(int mode) { BeginEdit(); selectingPair = mode != 0; Mode = mode; if (Mode == 2 && Probe != null) Probe = Probe with { IsEdge = false }; Armed = mode == 0 ? null : 0; Hint = mode == 0 ? "" : "Drag from A to B, or click each endpoint. Esc cancels."; Notify(); }
    public void SetSnapMode(int mode) { Mode = mode == 2 ? 2 : 1; if (Probe != null) PlaceProbe(Capture!.Signals.First(s => s.Id == Probe.Channel), Probe.Sample); Notify(); }
    public void Arm(int endpoint) { BeginEdit(); selectingPair = false; if (Mode == 0) Mode = 1; Armed = endpoint; Hint = $"Choose {(endpoint == 0 ? "A" : "B")} on any signal. Esc cancels."; Notify(); }
    public void CancelPick()
    {
        if (editOrigin is { } old) { A = old.A; B = old.B; Mode = old.Mode; PinnedMeasurement = old.Measurement; }
        editOrigin = null; selectingPair = draggingEndpoint = false; Armed = null; Hint = ""; Notify();
    }
    public void Clear() { A = B = null; PinnedMeasurement = null; editOrigin = null; selectingPair = draggingEndpoint = false; Armed = null; Mode = 0; Hint = ""; Notify(); }
    public void BeginDrag() { BeginEdit(); draggingEndpoint = true; }
    public void FinishDrag() { editOrigin = null; selectingPair = draggingEndpoint = false; Armed = null; Hint = ""; Notify(); }
    public void MeasurementError(string message) { Hint = message; Notify(); }
    public void ClearMeasurementError(string message) { if (Hint == message) { Hint = ""; Notify(); } }
    public void FocusProbe()
    {
        if (Capture == null || Signals.Count == 0) { Probe = null; return; }
        if (Probe is { } existing && Signals.Any(s => s.Id == existing.Channel) && existing.Sample >= Start && existing.Sample <= End)
        { SignalFocusRequested?.Invoke(existing.Channel); return; }
        PlaceProbe(Signals[0], (long)Math.Round((Start + End) / 2)); Notify(); SignalFocusRequested?.Invoke(Probe!.Channel);
    }
    private void PlaceProbe(Signal signal, long sample)
    {
        if (Mode == 2) { Probe = new(signal.Id, Math.Clamp(sample, 0, Capture!.SampleCount - 1), false); return; }
        var edge = Capture!.NearestObservedEdge(signal, sample);
        Probe = edge != null && edge.Sample >= Start && edge.Sample <= End
            ? new(signal.Id, edge.Sample, true)
            : new(signal.Id, Math.Clamp(sample, 0, Capture.SampleCount - 1), false);
    }
    public void MoveProbe(int channelDirection, int timeDirection)
    {
        FocusProbe(); if (Probe == null || Capture == null) return;
        if (channelDirection != 0)
        {
            int index = Signals.ToList().FindIndex(s => s.Id == Probe.Channel);
            PlaceProbe(Signals[Math.Clamp(index + channelDirection, 0, Signals.Count - 1)], Probe.Sample);
        }
        else if (timeDirection != 0)
        {
            if (Mode == 2) Probe = Probe with { Sample = Math.Clamp(Probe.Sample + Math.Sign(timeDirection), 0, Capture.SampleCount - 1), IsEdge = false };
            else
            {
                var signal = Signals.First(s => s.Id == Probe.Channel);
                var edge = timeDirection > 0 ? Capture.ObservedEdgeAtOrAfter(signal, Probe.Sample + 1) : Capture.ObservedEdgeBefore(signal, Probe.Sample);
                if (edge == null) { Hint = "No further observed edge on this signal."; Notify(); SignalFocusRequested?.Invoke(Probe.Channel); return; }
                Probe = new(signal.Id, edge.Sample, true);
            }
            if (Probe.Sample < Start || Probe.Sample > End)
            {
                Remember(); var half = (End - Start) / 2; SetRange(Probe.Sample - half, Probe.Sample + half);
            }
        }
        Hint = ""; Notify(); SignalFocusRequested?.Invoke(Probe.Channel);
    }
    public void CommitProbe()
    {
        FocusProbe(); if (Probe is not { } probe) return;
        if (Armed != null) { Pick(probe.Channel, probe.Sample, 0); SignalFocusRequested?.Invoke(probe.Channel); }
        else
        {
            var local = Capture!.MeasureAt(probe.Channel, probe.Sample);
            if (local.Pulse is not { } pulse) { MeasurementError($"D{probe.Channel} · {local.Reason}."); SignalFocusRequested?.Invoke(probe.Channel); return; }
            A = new(probe.Channel, pulse.Start, true); B = new(probe.Channel, pulse.End, true); PinnedMeasurement = local;
            Mode = 0; editOrigin = null; selectingPair = draggingEndpoint = false; Hint = ""; Notify();
            MeasurementFocusRequested?.Invoke([probe.Channel]);
        }
    }
    public bool Pick(int channel, long sample, double tolerance, int? replacing = null)
    {
        var signal = Capture?.Signals.FirstOrDefault(s => s.Id == channel);
        if (signal == null) return false;
        if (Mode == 0 && replacing == null)
        {
            var measurement = Capture!.MeasureAt(channel, sample);
            if (measurement.Pulse is not { } pulse) { Hint = $"D{channel} · {measurement.Reason}."; Notify(); return false; }
            PinnedMeasurement = measurement; A = new(channel, pulse.Start, true); B = new(channel, pulse.End, true);
            editOrigin = null; Hint = ""; Notify(); MeasurementFocusRequested?.Invoke([channel]); return true;
        }
        int? target = replacing ?? Armed;
        if (target == null) { Hint = "Choose A or B to replace an endpoint, or drag an existing cursor."; Notify(); return false; }
        var edge = Capture!.NearestObservedEdge(signal, sample);
        if (Mode != 2 && (edge == null || Math.Abs(edge.Sample - sample) > tolerance || edge.Sample < Start || edge.Sample > End))
        { Hint = "No nearby edge. Zoom in or use A/B position."; Notify(); return false; }
        if (replacing != null && Mode == 0) Mode = 1;
        PinnedMeasurement = null;
        var point = new Endpoint(channel, Mode == 2 ? Math.Clamp(sample, 0, Capture!.SampleCount - 1) : edge!.Sample, Mode != 2);
        if (target == 0) A = point; else B = point;
        Armed = replacing == null && selectingPair && target == 0 ? 1 : null;
        if (Armed == null && !draggingEndpoint) { editOrigin = null; selectingPair = false; }
        Hint = Armed == 1 ? "A set. Choose B on any signal. Esc cancels." : "";
        Notify();
        return true;
    }
    public bool EditEndpoint(int index, double microseconds)
    {
        if (decimal.TryParse(microseconds.ToString("R", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out var time)) return EditEndpointTime(index, time);
        MeasurementError("Enter a finite numeric time in µs."); return false;
    }
    public bool EditEndpointTime(int index, decimal microseconds)
    {
        var old = index == 0 ? A : B;
        if (old == null || Capture == null) return false;
        string maximumText = TimeAxis.Microseconds(Capture.SampleCount - 1, Capture.SampleRate);
        decimal maximum = Math.Max((Capture.SampleCount - 1) * 1_000_000m / Capture.SampleRate, decimal.Parse(maximumText, CultureInfo.InvariantCulture));
        if (microseconds < 0 || microseconds > maximum) { MeasurementError($"Enter a time from 0 to {maximumText} µs."); return false; }
        var sample = (long)decimal.Round(microseconds * Capture.SampleRate / 1_000_000m);
        if (sample == old.Sample) return true;
        editOrigin = null; selectingPair = draggingEndpoint = false; Armed = null;
        var point = old with { Sample = sample, IsEdge = false };
        if (index == 0) A = point; else B = point;
        Mode = 2; PinnedMeasurement = null; Hint = $"Position · {TimingText.Duration(1d / Capture.SampleRate)} sample grid."; Notify(); return true;
    }
    public void FitMarkers()
    {
        if (A == null || B == null) return;
        Remember();
        if (!Group.Channels.Contains(A.Channel) || !Group.Channels.Contains(B.Channel))
            Group = Groups.FirstOrDefault(g => g.Channels.Contains(A.Channel) && g.Channels.Contains(B.Channel)) ?? Groups[^1];
        var first = Math.Min(A.Sample, B.Sample); var last = Math.Max(A.Sample, B.Sample);
        if (PinnedMeasurement?.Cycle is { } cycle) { first = Math.Min(first, cycle.Start); last = Math.Max(last, cycle.End); }
        var pad = Math.Max(5, (last - first) * .15);
        PacketsTab = false; SetRange(first - pad, last + pad);
        MeasurementFocusRequested?.Invoke(new[] { A.Channel, B.Channel }.Distinct().ToArray());
    }
    public void ShowEndpoint(int index)
    {
        var point = index == 0 ? A : index == 1 ? B : null;
        if (point == null || Capture?.Signals.Any(s => s.Id == point.Channel) != true) return;
        if (!Group.Channels.Contains(point.Channel) || point.Sample < Start || point.Sample > End) Remember();
        if (!Group.Channels.Contains(point.Channel))
            Group = Groups.First(g => g.Channels.Contains(point.Channel));
        PacketsTab = false;
        if (point.Sample < Start || point.Sample > End)
        {
            double half = (End - Start) / 2;
            SetRange(point.Sample - half, point.Sample + half);
        }
        else Notify();
        SignalFocusRequested?.Invoke(point.Channel);
    }
    public void StepEdge(int channel, int direction)
    {
        var signal = Capture?.Signals.FirstOrDefault(s => s.Id == channel); if (signal == null) return;
        var center = (Start + End) / 2;
        Edge? edge = direction > 0 ? Capture!.ObservedEdgeAtOrAfter(signal, (long)Math.Floor(center) + 1) : Capture!.ObservedEdgeBefore(signal, (long)Math.Ceiling(center));
        if (edge == null) { Hint = "No further edge in this capture."; Notify(); return; }
        Remember(); var span = End - Start; SetRange(edge.Sample - span / 2, edge.Sample + span / 2);
    }
}
