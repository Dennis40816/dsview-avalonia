// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using DSView.Desktop;
using DSView.Domain;
using DSView.Infrastructure;

internal static class DecoderDraftChecks
{
    private static Capture Raw() => new(1_000_000, 200,
        [new(19, "Clock", false, []), new(3, "TX", false, []), new(7, "RX", true, [])], []) { DecoderSources = [] };
    private static Workbench Model(Capture? capture = null) { var model = new Workbench(); model.Load(capture ?? Raw()); return model; }
    internal static void Map(DecoderDraft draft, string role, int id) => draft.Role(role).Selected = draft.Role(role).Channels.Single(c => c.Id == id);
    public static async Task RunAsync(Action<bool, string> check)
    {
        using (var vm = new DecoderViewModel(Model()))
        {
            vm.Open(); vm.Draft.Protocol = "SPI";
            Map(vm.Draft, "clk", 19);
            bool absent = !vm.DecodeCommand.CanExecute(null);
            Map(vm.Draft, "miso", 7); vm.Draft.Bits = "128"; vm.Draft.Mode = 3; vm.Draft.Polarity = "active-high";
            bool optional = vm.Draft.TryCreate(out var source, out _) && source!.Inputs.Length == 2
                && source.Spi == new SpiOptions(3, "msb-first", 128, "active-high");
            Map(vm.Draft, "mosi", 7); bool duplicate = !vm.DecodeCommand.CanExecute(null);
            Map(vm.Draft, "mosi", -1); vm.Draft.Bits = "129";
            check(absent && optional && duplicate && !vm.DecodeCommand.CanExecute(null), "SPI draft requires clock and data, keeps optional roles unmapped, and rejects duplicate signals or oversized words");
            vm.Draft.Protocol = "UART"; Map(vm.Draft, "data", 3); vm.Draft.Baud = "100000"; vm.Draft.Bits = "9"; vm.Draft.Parity = "even"; vm.Draft.Inverted = true;
            bool configured = vm.Draft.TryCreate(out source, out _) && source!.Uart == new UartOptions(100000, 9, "even", 1, "lsb-first", true);
            vm.Draft.Baud = "500001"; bool undersampled = !vm.DecodeCommand.CanExecute(null);
            vm.Draft.Baud = "115.2k";
            check(configured && undersampled && !vm.DecodeCommand.CanExecute(null), "UART draft preserves baud, width, parity and inversion; malformed or undersampled baud cannot start");
        }
        var legacy = Raw() with { DecoderSources = [new("spi", [new("clk", 19), new("mosi", 24)])] };
        using (var vm = new DecoderViewModel(Model(legacy)))
        {
            vm.Open(); vm.Target = vm.Targets.Single(t => t.Source?.Id == "spi");
            bool unknown = vm.Draft.HasUnknownSettings && !vm.Draft.CanChooseProtocol
                && vm.Draft.Role("mosi").Selected is { Id: 24, Available: false } && !vm.DecodeCommand.CanExecute(null);
            Map(vm.Draft, "mosi", 3); vm.Draft.Protocol = "UART";
            check(unknown && vm.Draft.Protocol == "SPI" && vm.DecodeCommand.CanExecute(null) && legacy.DecoderSources[0].Spi == null,
                "Unknown saved options stay unknown and missing physical roles stay visible until explicitly remapped");
        }
        foreach (string protocol in new[] { "SPI", "UART" })
        {
            var model = Model(); var before = model.Capture!; DecoderSource? request = null;
            var gate = new TaskCompletionSource<IDecoderResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var vm = new DecoderViewModel(model, (_, s, _) => { request = s; return gate.Task; }); vm.Open(); vm.Draft.Protocol = protocol;
            if (protocol == "SPI") { Map(vm.Draft, "clk", 19); Map(vm.Draft, "mosi", 3); } else Map(vm.Draft, "data", 7);
            var work = vm.DecodeCommand.ExecuteAsync(null); var draft = vm.Draft; string bits = draft.Bits;
            draft.Bits = "16"; vm.Target = vm.Targets[0]; draft.Protocol = "I²C"; vm.CancelCommand.Execute(null);
            gate.SetResult(new SerialDecodeResult(before, request!, [])); await work.WaitAsync(TimeSpan.FromSeconds(10));
            check(ReferenceEquals(model.Capture, before) && draft.Bits == bits && draft.Protocol == protocol && vm.DecodeCommand.CanExecute(null),
                $"{protocol} cancellation rejects late success and locks protocol settings during work");
        }
        await Publication(check);
        var moving = Model();
        using var panel = new DecoderViewModel(moving); panel.Open();
        int notifications = 0; panel.PropertyChanged += (_, _) => notifications++;
        for (int i = 0; i < 100; i++) moving.SetRange(i, i + 50);
        check(notifications == 0 && moving.Start == 99 && moving.End == 149,
            "Viewport movement does not repeatedly invalidate unrelated decoder controls");
    }
    private static async Task Publication(Action<bool, string> check)
    {
        var model = Model(); model.SetMode(2); model.Pick(19, 25, 0); model.Pick(3, 75, 0); model.SetRange(10, 100);
        var view = (model.Group, model.A, model.B, model.Start, model.End);
        bool mismatch = false;
        using var vm = new DecoderViewModel(model, (c, source, _) => Task.FromResult<IDecoderResult>(new SerialDecodeResult(c,
            mismatch ? source with { Spi = source.Spi! with { Mode = (source.Spi.Mode + 1) % 4 } } : source, [])));
        vm.Open(); vm.Draft.Protocol = "SPI"; Map(vm.Draft, "clk", 19); Map(vm.Draft, "mosi", 3);
        await vm.DecodeCommand.ExecuteAsync(null);
        bool first = vm.Target?.Source?.Id == "spi" && model.Capture!.DecoderSources!.Length == 1 && view == (model.Group, model.A, model.B, model.Start, model.End);
        vm.Target = vm.Targets[0]; vm.Draft.Protocol = "SPI"; Map(vm.Draft, "clk", 19); Map(vm.Draft, "miso", 7);
        await vm.DecodeCommand.ExecuteAsync(null);
        bool second = vm.Target?.Source?.Id == "spi-1" && model.Capture!.DecoderSources!.Length == 2;
        var current = model.Capture; mismatch = true; vm.Draft.Mode = 2; await vm.DecodeCommand.ExecuteAsync(null);
        check(first && second && ReferenceEquals(current, model.Capture) && vm.Status.Contains("different source or settings"),
            "Shared publication preserves Group/view/A/B, creates distinct instances and rejects mismatched native options");
        vm.Close();
        var saturated = Raw() with { DecoderSources = Enumerable.Range(0, 32).Select(i => new DecoderSource("uart-" + i, [new("data", 3)]) { Protocol = "uart" }).ToArray() };
        using var full = new DecoderViewModel(Model(saturated)); full.Open(); Map(full.Draft, "scl", 19); Map(full.Draft, "sda", 3);
        check(!full.DecodeCommand.CanExecute(null), "Decoder instance limit is enforced before worker startup");
    }
}
