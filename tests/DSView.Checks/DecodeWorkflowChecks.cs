// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using DSView.Desktop;
using DSView.Domain;
using DSView.Infrastructure;

internal static class DecodeWorkflowChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        check(RoleAdmission(), "Decoder roles require distinct captured signals; gaps and pending edits cannot start");
        check(CancelDraft(), "Closing a role draft does not change saved decoder sources");
        check(await Success(), "Decode publishes chosen roles while preserving view and measurements; reveal finds the mapped Group");
        check(await Cancel(), "Cancelled decoder cannot publish even when its worker returns success");
        check(await ReplaceSource(), "Switching capture cancels work and rejects its late result");
        check(await CloseAndReopen(), "Closing and reopening cannot admit a prior generation or overlap workers");
        check(await FailedThenRetry(), "Decode failure keeps prior analysis and permits retry");
        check(await PendingEdit(), "Cursor edit begun during decode prevents replacement without losing the draft");
        check(await WrongResult(), "Wrong-source or wrong-role decoder output is not published");
        check(await Dispose(), "Disposed decoder cancels work and rejects late publication");
        check(await StopWaits(), "Shutdown waits for cancelled worker completion before releasing the decoder");
    }
    private static Capture Raw() => new(1_000_000, 100,
        [new(19, "SCL", false, [new(10, true), new(20, false)]), new(3, "SDA", true, [new(15, false)])], []);
    private static Workbench Model() { var model = new Workbench(); model.Load(Raw()); return model; }
    private static void Choose(I2cDecoderViewModel vm) { vm.Open(); vm.Scl = vm.Channels.Single(c => c.Id == 19); vm.Sda = vm.Channels.Single(c => c.Id == 3); }
    private static TaskCompletionSource<I2cDecodeResult> Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static async Task Finish(Task work) => await work.WaitAsync(TimeSpan.FromSeconds(10));
    private static bool RoleAdmission()
    {
        var model = Model(); using var vm = new I2cDecoderViewModel(model); vm.Open();
        bool absent = !vm.DecodeCommand.CanExecute(null); Choose(vm);
        bool valid = vm.DecodeCommand.CanExecute(null); vm.Sda = vm.Scl;
        bool duplicate = !vm.DecodeCommand.CanExecute(null); Choose(vm); model.Arm(0);
        bool pending = !vm.DecodeCommand.CanExecute(null); model.CancelPick();
        model.Load(Raw() with { Gaps = [new(30, 40)] }); Choose(vm);
        return absent && valid && duplicate && pending && !vm.DecodeCommand.CanExecute(null);
    }
    private static bool CancelDraft()
    {
        var model = Model(); var before = model.Capture; using var vm = new I2cDecoderViewModel(model);
        Choose(vm); vm.Close(); return ReferenceEquals(model.Capture, before) && !vm.IsOpen;
    }
    private static async Task<bool> Success()
    {
        var model = Model(); model.SetMode(2); model.Pick(19, 11, 0); model.Pick(3, 23, 0); model.SetRange(5, 50);
        var before = (model.A, model.B, model.Start, model.End); var old = model.Capture;
        using var vm = new I2cDecoderViewModel(model, (c, m, _) => Task.FromResult(new I2cDecodeResult(c, m, [])));
        Choose(vm); await Finish(vm.DecodeCommand.ExecuteAsync(null));
        bool published = !ReferenceEquals(old, model.Capture) && (model.A, model.B, model.Start, model.End) == before
            && model.Capture!.ResolvedDecoders.Single(d => d.Id == "i2c").Channels.SequenceEqual(new[] { 19, 3 });
        vm.ShowResultCommand.Execute(null);
        return published && model.Group.Channels.Contains(19) && model.Group.Channels.Contains(3) && !vm.IsOpen;
    }
    private static async Task<bool> Cancel()
    {
        var model = Model(); var original = model.Capture!; var gate = Gate(); CancellationToken token = default;
        using var vm = new I2cDecoderViewModel(model, (_, _, t) => { token = t; return gate.Task; }); Choose(vm);
        var work = vm.DecodeCommand.ExecuteAsync(null); var scl = vm.Scl; vm.Scl = vm.Sda;
        bool locked = vm.Scl == scl && !vm.DecodeCommand.CanExecute(null); vm.CancelCommand.Execute(null);
        gate.SetResult(new(original, new(19, 3), [])); await Finish(work);
        return locked && token.IsCancellationRequested && ReferenceEquals(original, model.Capture) && vm.DecodeCommand.CanExecute(null);
    }
    private static async Task<bool> ReplaceSource()
    {
        var model = Model(); var original = model.Capture!; var gate = Gate(); CancellationToken token = default;
        using var vm = new I2cDecoderViewModel(model, (_, _, t) => { token = t; return gate.Task; }); Choose(vm);
        var work = vm.DecodeCommand.ExecuteAsync(null); var replacement = Raw() with { SampleRate = 2_000_000 }; model.Load(replacement);
        gate.SetResult(new(original, new(19, 3), [])); await Finish(work);
        return token.IsCancellationRequested && ReferenceEquals(replacement, model.Capture) && !vm.ShowResultCommand.CanExecute(null);
    }
    private static async Task<bool> CloseAndReopen()
    {
        var model = Model(); var original = model.Capture!; var gate = Gate(); int calls = 0;
        using var vm = new I2cDecoderViewModel(model, (_, _, _) => { calls++; return gate.Task; }); Choose(vm);
        var work = vm.DecodeCommand.ExecuteAsync(null); vm.Close(); vm.Open(); bool blocked = !vm.DecodeCommand.CanExecute(null);
        gate.SetResult(new(original, new(19, 3), [])); await Finish(work);
        return blocked && calls == 1 && ReferenceEquals(original, model.Capture) && vm.IsOpen && vm.DecodeCommand.CanExecute(null);
    }
    private static async Task<bool> FailedThenRetry()
    {
        var model = Model(); var original = model.Capture; int calls = 0;
        using var vm = new I2cDecoderViewModel(model, (c, m, _) => ++calls == 1
            ? Task.FromException<I2cDecodeResult>(new IOException("Worker unavailable")) : Task.FromResult(new I2cDecodeResult(c, m, [])));
        Choose(vm); await Finish(vm.DecodeCommand.ExecuteAsync(null));
        bool kept = ReferenceEquals(original, model.Capture) && vm.Status.Contains("Worker unavailable") && vm.DecodeCommand.CanExecute(null);
        await Finish(vm.DecodeCommand.ExecuteAsync(null)); return kept && !ReferenceEquals(original, model.Capture);
    }
    private static async Task<bool> PendingEdit()
    {
        var model = Model(); var original = model.Capture!; var gate = Gate();
        using var vm = new I2cDecoderViewModel(model, (_, _, _) => gate.Task); Choose(vm);
        var work = vm.DecodeCommand.ExecuteAsync(null); model.Arm(0); gate.SetResult(new(original, new(19, 3), [])); await Finish(work);
        return ReferenceEquals(original, model.Capture) && model.Armed == 0 && !vm.DecodeCommand.CanExecute(null);
    }
    private static async Task<bool> WrongResult()
    {
        foreach (bool wrongSource in new[] { true, false })
        {
            var model = Model(); var original = model.Capture;
            using var vm = new I2cDecoderViewModel(model, (c, m, _) => Task.FromResult(new I2cDecodeResult(wrongSource ? Raw() : c, wrongSource ? m : new(3, 19), [])));
            Choose(vm); await Finish(vm.DecodeCommand.ExecuteAsync(null)); if (!ReferenceEquals(original, model.Capture) || vm.ShowResultCommand.CanExecute(null)) return false;
        }
        return true;
    }
    private static async Task<bool> Dispose()
    {
        var model = Model(); var original = model.Capture!; var gate = Gate(); CancellationToken token = default;
        var vm = new I2cDecoderViewModel(model, (_, _, t) => { token = t; return gate.Task; }); Choose(vm);
        var work = vm.DecodeCommand.ExecuteAsync(null); vm.Dispose(); gate.SetResult(new(original, new(19, 3), [])); await Finish(work); vm.Open();
        return token.IsCancellationRequested && !vm.IsOpen && !vm.DecodeCommand.CanExecute(null) && ReferenceEquals(original, model.Capture);
    }
    private static async Task<bool> StopWaits()
    {
        var model = Model(); var original = model.Capture!; var gate = Gate(); CancellationToken token = default;
        using var vm = new I2cDecoderViewModel(model, (_, _, t) => { token = t; return gate.Task; }); Choose(vm);
        var work = vm.DecodeCommand.ExecuteAsync(null); var stop = vm.StopAsync();
        bool waiting = !stop.IsCompleted && token.IsCancellationRequested;
        gate.SetResult(new(original, new(19, 3), [])); await Finish(stop); await Finish(work);
        return waiting && ReferenceEquals(original, model.Capture) && !vm.IsOpen;
    }
}
