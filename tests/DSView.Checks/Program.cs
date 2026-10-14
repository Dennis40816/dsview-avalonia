// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using DSView.Desktop;
using DSView.Domain;
using DSView.Infrastructure;

int passed = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); passed++; }
var capture = await DemoCaptureSource.LoadAsync();
var clock = capture.MeasureAt(0, 25025);
Check(clock is { High: false, WidthSamples: 63, PeriodSamples: 125, HighSamples: 62 } && TimingText.Local(clock, capture.SampleRate).Contains("400 kHz"), "Low width and complete same-polarity cycle yield 1.26 us and 400 kHz");
Check(capture.MeasureAt(0, 0).Pulse == null && capture.MeasureAt(0, capture.SampleCount - 1).Pulse == null, "Capture boundaries never substitute a remote pulse");
var smallSignal = new Signal(0, "Test clock", false, [new(10, true), new(30, false), new(60, true), new(80, false), new(110, true)]);
var small = new Capture(1000, 125, [smallSignal], []);
Check(small.MeasureAt(0, 20) is { High: true, WidthSamples: 20, PeriodSamples: 50 } && TimingText.Local(small.MeasureAt(0, 20), small.SampleRate).Contains("20 Hz"), "Changed-rate fixture: 20 ms high pulse, 50 ms period, 20 Hz");
Check(small.MeasureAt(0, 30) is { High: false, WidthSamples: 30, PeriodSamples: 50 }, "Exact transition selects the following pulse");
Check(small.MeasureAt(0, 9).Pulse == null && small.MeasureAt(0, 110).Pulse == null && small.MeasureAt(0, 125).Pulse == null, "Both boundaries and out-of-capture positions remain incomplete");
Check((small with { Signals = [smallSignal with { Edges = [new(10, true), new(30, false)] }] }).MeasureAt(0, 20) is { WidthSamples: 20, PeriodSamples: null }, "Two transitions provide width but no invented period");
Check((small with { Gaps = [new(15, 16)] }).MeasureAt(0, 20).Pulse == null, "Missing samples invalidate a pulse");
Check((small with { Gaps = [new(45, 50)] }).MeasureAt(0, 20) is { WidthSamples: 20, PeriodSamples: null }, "Cycle-only gap preserves width but invalidates frequency");
Check((small with { Gaps = [new(0, 10)] }).MeasureAt(0, 20).PeriodSamples == 50 && (small with { Gaps = [new(30, 31)] }).MeasureAt(0, 20).Pulse == null, "Gap end is observable but a bounding edge at gap start is missing");
Check((small with { Signals = [new(0, "Quiet", true, [])] }).MeasureAt(0, 20).Pulse == null, "Quiet signal has no invented pulse");
Check(capture.Signals.Count == 32 && capture.Signals.Select(s => s.Id).SequenceEqual(Enumerable.Range(0, 32)), "32 physical channels in source order");
Check(capture.Signals.All(s => s.Edges.Zip(s.Edges.Skip(1)).All(p => p.First.Sample < p.Second.Sample && p.First.High != p.Second.High)), "Transition lists are ordered and contain real state changes");
var vm = new Workbench(); vm.Load(capture);
vm.Pick(0, 25025, 5);
Check(vm.A is { Sample: 25000, IsEdge: true } && vm.B is { Sample: 25063, IsEdge: true }, "Adjacent SCL edges preserve 20 ns samples and 1.26 us pulse width");
Check(vm.EditEndpoint(0, 500) && vm.Mode == 0 && vm.A!.IsEdge, "Unchanged endpoint text preserves edge metadata and mode");
var pinned = (vm.A, vm.B, vm.Mode, vm.Armed, vm.Start, vm.End, vm.PinnedMeasurement);
capture.MeasureAt(8, 38500); vm.Pick(0, 0, 5);
Check((vm.A, vm.B, vm.Mode, vm.Armed, vm.Start, vm.End, vm.PinnedMeasurement) == pinned, "Pure preview and incomplete click preserve pinned timing state");
vm.SetMode(1); vm.Pick(0, 25063, 5); vm.CancelPick();
Check((vm.A, vm.B, vm.Mode, vm.Armed, vm.Start, vm.End, vm.PinnedMeasurement) == pinned, "Cancel two-point selection restores prior endpoints and local result");
vm.Pick(0, 25188, 5, 1);
Check(vm.Mode == 1 && vm.PinnedMeasurement == null && vm.A?.Sample == 25000 && vm.B?.Sample == 25188, "Dragging a quick marker becomes manual and preserves the other endpoint");
vm.SetMode(1); vm.Pick(0, 25000, 5); vm.SetRange(0, 50000); vm.Pick(8, 38000, 5);
Check(vm.A is { Channel: 0, Sample: 25000 } && vm.B is { Channel: 8, Sample: 38000 }, "Manual edges can span channels");
var fixedB = vm.B; vm.Arm(0); vm.Pick(0, 25063, 5);
Check(vm.A?.Sample == 25063 && vm.B == fixedB && vm.Armed == null, "Replacing A completes without arming or changing B");
vm.Pick(8, 38750, 5); Check(vm.B == fixedB, "A later ordinary click cannot overwrite B");
var old = vm.B; vm.Arm(1); vm.Pick(8, 20000, 4); Check(vm.B == old, "Missed edge preserves previous endpoint");
vm.SetMode(2); vm.Pick(1, 29006, 0); Check(vm.A is { Channel: 1, Sample: 29006, IsEdge: false }, "Position endpoints retain integer samples");
var packet = capture.Packets.First(p => p.Id == "ctl-2"); var items = capture.ProtocolItems(packet);
Check(items.Count == 16 && items.Count(i => i.Kind == "ack") == 7 && items.Where(i => i.Kind == "ack").All(i => i.Span.End - i.Span.Start == 125), "I2C ACKs have separate one-clock source spans");
Check(items.First().Span.Start == items.First().Span.End && items.Last().Span.Start == items.Last().Span.End, "START and STOP are points");
Check(packet.Value == 0x1235 && packet.Error != null && items.Where(i => i.Kind == "ack").All(i => i.Label == "ACK"), "Application checksum error does not change bus ACKs");
vm.Select(packet); var before = (vm.Start, vm.End, vm.A, vm.B);
vm.SetGroup(new("Reset only", [8], [])); Check(!vm.Packets.Any() && !vm.SelectionInGroup && (vm.Start, vm.End, vm.A, vm.B) == before, "Raw-only group has no unrelated packets and preserves timing context");
vm.ShowAll(); Check(vm.Signals.Count == 32 && vm.Packets.Count() == 12, "Explicit All signals exposes the complete source");
vm.SetRange(-400, 600); Check(vm.Start == 0 && vm.End == 1000, "Panning clamps without shrinking viewport");
vm.SetRange(49500, 50500); Check(vm.Start == 49000 && vm.End == 50000, "Right capture boundary preserves viewport span");
vm.Search("0x1235"); Check(vm.Packets.Single().Id == "ctl-2", "Hex search matches interpreted payload");
vm.Load(small);
Check(vm.A == null && vm.B == null && vm.PinnedMeasurement == null && vm.Selected == null && vm.Armed == null && vm.Start == 0 && vm.End == 125, "Replacing capture clears old source measurements, selection and view");
vm.Previous(); Check(vm.Start == 0 && vm.End == 125, "Old-source navigation history cannot return after loading another capture");
using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
try { await DemoCaptureSource.LoadAsync(cancelled.Token); throw new Exception("Cancellation was ignored"); } catch (OperationCanceledException) { Check(true, "Background source loading honors cancellation"); }
Console.WriteLine($"{passed} focused checks passed.");
