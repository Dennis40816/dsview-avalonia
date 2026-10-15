// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using DSView.Checks.Support;
using DSView.Desktop;
using DSView.Domain;
using DSView.Infrastructure;

internal static class AcquisitionWorkflowChecks
{
    private static Workbench Model() { var m = new Workbench(); m.Load(new(1000,100,[new(0,"Old",false,[new(10,true),new(30,false)])],[]) { DecoderSources=[] }); return m; }
    private static TaskCompletionSource Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static Task Finish(Task task) => task.WaitAsync(TimeSpan.FromSeconds(10));
    private sealed class Source(Func<AcquisitionWriter,CancellationToken,CancellationToken,Task<AcquisitionTermination>> run) : IAcquisitionSource
    {
        public AcquisitionPlan Plan { get; } = new(AcquisitionSourceKind.Demo,"Clock",1000,20,[new(7,"CLOCK")]);
        public Task<AcquisitionTermination> AcquireAsync(AcquisitionWriter writer,CancellationToken stop,CancellationToken abort) => run(writer,stop,abort);
    }
    private static Source Complete() => new((w,_,_) => { w.Running(); w.TryWrite(0,[0,0,0,0,128,128,128,128,0,0,0,0,128,128,128,128,0,0,0,0]); return Task.FromResult(AcquisitionTermination.Completed); });
    public static async Task RunAsync(Action<bool,string> check)
    {
        var draft = new DemoCaptureDraft();
        check(draft.TryCreate(out var settings)=="" && settings == new DemoCaptureSettings(0,1_000_000,1_000_000,50),
            "Demo defaults define one second at 1 MS/s and a 10 kHz clock");
        draft.Frequency="333333"; check(draft.TryCreate(out _)!="", "Demo rejects a frequency that cannot fit the integer sample grid");
        draft.Frequency="10000"; draft.Duration="0.0001"; check(draft.TryCreate(out _)!="", "Demo cannot request a fractional or too-short sample capture");
        draft.Duration="1000"; draft.Channel="31"; check(draft.TryCreate(out settings)=="" && settings!.Channel==31,"Demo settings preserve physical channel D31");
        draft.Scenario="I²C + Interrupt"; draft.DataChannel="4"; draft.InterruptChannel="9";
        check(draft.TryCreate(out settings)=="" && settings is { I2c:true,Channel:31,DataChannel:4,InterruptChannel:9,HalfPeriod:50 },
            "I2C scenario settings retain independent SCL, SDA and Interrupt mapping");
        draft.DataChannel="31"; check(draft.TryCreate(out _)!="","I2C scenario rejects duplicated physical roles");
        draft.DataChannel="4"; draft.Frequency="100000"; check(draft.TryCreate(out _)!="","I2C scenario requires four whole sample phases, not a rounded clock");
        draft.Scenario="Unknown"; check(draft.TryCreate(out _)!="","Unknown Demo scenarios are not silently treated as clock captures");

        var model=Model(); var old=model.Capture; int published=0;
        await using (var vm=new AcquisitionViewModel(model,()=>true,()=>Task.FromResult(true),_=>published++,_=>Complete()))
        {
            await Finish(vm.StartCommand.ExecuteAsync(null));
            check(published==1 && !ReferenceEquals(old,model.Capture) && model.Capture!.Signals.Single().Id==7 && model.Group.Name=="All signals"
                && model.Capture.MeasureAt(7,5) is { WidthSamples:4,PeriodSamples:8 } && !vm.HasResult && vm.StartCommand.CanExecute(null),
                "Completed acquisition admits a validated raw capture with correct timing and an immediately visible channel");
            using var files=new TestWorkspace(); string path=Path.Combine(files.Root,"clock.dsview2");
            var document=new AnalysisDocument(AnalysisDocument.FileFormat,AnalysisDocument.CurrentVersion,"Demo · Clock",model.Capture!,model.SnapshotState(new(null,0),7));
            await AnalysisFile.WriteAsync(path,document); var reopened=await AnalysisFile.ReadAsync(path);
            check(reopened.Capture.SourceDescription.StartsWith("Demo:") && reopened.Capture.SourceDescription.Contains("Completed")
                && reopened.Capture.Signals[0].Edges.SequenceEqual(model.Capture!.Signals[0].Edges) && reopened.Capture.DecoderSources!.Length==0,
                "Acquired Demo raw timing and current provenance survive the existing analysis save/reopen path");
        }
        model=Model(); old=model.Capture; bool permit=false;
        await using (var vm=new AcquisitionViewModel(model,()=>true,()=>Task.FromResult(permit),_=>{},_=>Complete()))
        {
            await Finish(vm.StartCommand.ExecuteAsync(null));
            check(ReferenceEquals(old,model.Capture) && vm.HasResult && vm.IsOpen && !vm.StartCommand.CanExecute(null),
                "Declining replacement keeps the original analysis and retains a ready result for later");
            vm.CloseCommand.Execute(null); vm.PrimaryCommand.Execute(null);
            check(vm.PrimaryLabel=="Review" && vm.IsOpen && ReferenceEquals(old,model.Capture),
                "A retained capture remains discoverable through Review after its setup panel is closed");
            permit=true; await Finish(vm.AcceptCommand.ExecuteAsync(null));
            check(!ReferenceEquals(old,model.Capture) && !vm.HasResult,"Explicitly showing a ready capture retries replacement without reacquiring");
        }
        model=Model(); old=model.Capture; var entered=Gate();
        await using (var vm=new AcquisitionViewModel(model,()=>true,()=>Task.FromResult(true),_=>{},_=>new Source(async(w,stop,abort)=>
        {
            w.Running(); w.TryWrite(0,new uint[12]); entered.SetResult();
            using var linked=CancellationTokenSource.CreateLinkedTokenSource(stop,abort);
            try { await Task.Delay(Timeout.Infinite,linked.Token); } catch(OperationCanceledException) { }
            return AcquisitionTermination.Stopped;
        })))
        {
            var job=vm.StartCommand.ExecuteAsync(null); await Finish(entered.Task); vm.Poll();
            check(vm.PrimaryLabel=="Stop" && vm.PrimaryCommand.CanExecute(null) && ReferenceEquals(old,model.Capture),
                "Capture keeps the current waveform available and exposes Stop as the primary action");
            vm.StopCommand.Execute(null); await Finish(job);
            check(ReferenceEquals(old,model.Capture) && vm.HasResult && vm.ResultLabel=="Use partial capture", "Stopped samples require explicit partial acceptance");
            await Finish(vm.AcceptCommand.ExecuteAsync(null));
            check(model.Capture!.SampleCount==12 && model.Capture.SourceDescription.Contains("Stopped"),"Partial acceptance publishes only the observed sample count with termination provenance");
        }
        foreach(bool replace in new[]{false,true})
        {
            model=Model(); old=model.Capture; entered=Gate(); var release=Gate();
            await using var vm=new AcquisitionViewModel(model,()=>true,()=>Task.FromResult(true),_=>{},_=>new Source(async(w,_,_)=>
            { w.Running(); entered.SetResult(); await release.Task; w.TryWrite(0,new uint[20]); return AcquisitionTermination.Completed; }));
            var job=vm.StartCommand.ExecuteAsync(null); await Finish(entered.Task);
            Task? stopping=null; Capture? replacement=null;
            try
            {
                if(replace) { replacement=old! with { SampleRate=2000 }; model.Load(replacement); }
                else { stopping=vm.StopAsync(); check(!stopping.IsCompleted,"Source switching waits for an uncooperative producer to actually return"); }
            }
            finally { release.TrySetResult(); }
            await Finish(job); if(stopping!=null) await Finish(stopping);
            check(ReferenceEquals(model.Capture,replace?replacement:old) && !vm.HasResult && vm.StartCommand.CanExecute(null),
                replace?"Changing the current capture rejects an old acquisition's late success":"Explicit cancellation rejects late success and permits a fresh acquisition after cleanup");
        }
        model=Model(); old=model.Capture; model.Arm(0);
        await using (var vm=new AcquisitionViewModel(model,()=>true,()=>Task.FromResult(true),_=>{},_=>Complete()))
        {
            await Finish(vm.StartCommand.ExecuteAsync(null));
            check(ReferenceEquals(old,model.Capture) && vm.HasResult && !vm.AcceptCommand.CanExecute(null),"A pending cursor edit prevents automatic or explicit capture replacement");
            model.CancelPick(); await Finish(vm.AcceptCommand.ExecuteAsync(null));
            check(!ReferenceEquals(old,model.Capture),"Finishing the cursor edit enables the retained capture without losing it");
        }
        model=Model(); old=model.Capture; bool available=false;
        await using (var vm=new AcquisitionViewModel(model,()=>available,()=>Task.FromResult(true),_=>{},_=>Complete()))
        {
            check(!vm.StartCommand.CanExecute(null),"File/settings activity can inhibit acquisition through the host boundary");
            available=true; await Finish(vm.StartCommand.ExecuteAsync(null));
            check(!ReferenceEquals(old,model.Capture),"Acquisition becomes available after the host operation ends");
        }
    }
}
