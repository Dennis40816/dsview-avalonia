// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using DSView.Desktop;
using DSView.Domain;
using DSView.Infrastructure;

int passed = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); passed++; }
var capture = await DemoCaptureSource.LoadAsync();
Check(capture.Signals.Count == 32 && capture.Signals.Select(s => s.Id).SequenceEqual(Enumerable.Range(0, 32)), "32 physical channels in source order");
Check(capture.Signals.All(s => s.Edges.Zip(s.Edges.Skip(1)).All(p => p.First.Sample < p.Second.Sample && p.First.High != p.Second.High)), "Transition lists are ordered and contain real state changes");
var vm = new Workbench(); vm.Load(capture);
vm.Pick(0, 25025, 5);
Check(vm.A is { Sample: 25000, IsEdge: true } && vm.B is { Sample: 25063, IsEdge: true }, "Adjacent SCL edges preserve 20 ns samples and 1.26 us pulse width");
Check(vm.EditEndpoint(0, 500) && vm.Mode == 0 && vm.A!.IsEdge, "Unchanged endpoint text preserves edge metadata and mode");
vm.SetMode(1); vm.Pick(0, 25000, 5); vm.SetRange(0, 50000); vm.Pick(8, 38000, 5);
Check(vm.A is { Channel: 0, Sample: 25000 } && vm.B is { Channel: 8, Sample: 38000 }, "Manual edges can span channels");
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
using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
try { await DemoCaptureSource.LoadAsync(cancelled.Token); throw new Exception("Cancellation was ignored"); } catch (OperationCanceledException) { Check(true, "Background source loading honors cancellation"); }
Console.WriteLine($"{passed} focused checks passed.");
