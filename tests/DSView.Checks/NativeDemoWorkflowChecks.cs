// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using DSView.Checks.Support;
using DSView.Desktop;
using DSView.Domain;
using DSView.Infrastructure;

internal static class NativeDemoWorkflowChecks
{
    public static async Task RunAsync(Action<bool,string> check, string configuration)
    {
        var options=NativeI2cSource.ReadConfiguration(configuration);
        var model=new Workbench(); Task? decodeTask=null;
        using var decoder=new DecoderViewModel(model,async(raw,binding,token)=>await NativeI2cSource.DecodeMappedAsync(raw,
            new(binding.Inputs.Single(i=>i.Role=="scl").Channel,binding.Inputs.Single(i=>i.Role=="sda").Channel),options,token,binding.Id));
        await using var acquisition=new AcquisitionViewModel(model,()=>!decoder.IsRunning,()=>Task.FromResult(true),_=>{},decodeRequested:id=>
        {
            decoder.Open(); decoder.Target=decoder.Targets.Single(t=>t.Source?.Id==id);
            decodeTask=decoder.DecodeCommand.ExecuteAsync(null);
        });
        var draft=acquisition.Draft;
        draft.Scenario="I²C + Interrupt"; draft.Channel="31"; draft.DataChannel="4"; draft.InterruptChannel="9";
        draft.Rate="2000000"; draft.Duration="2.56"; draft.Frequency="100000";
        await acquisition.StartCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(10));
        var raw=model.Capture!;
        check(raw is { SampleRate:2_000_000,SampleCount:5120,Packets.Count:0 } && raw.Signals.Select(s=>s.Id).SequenceEqual(new[]{4,9,31})
            && raw.EffectiveDecoderSources.Single().SameConfiguration(new("demo-i2c",[new("scl",31),new("sda",4)]){Protocol="i2c"})
            && acquisition.DecodeDemoCommand.CanExecute(null),
            "Actual I2C Demo acquisition publishes raw samples plus explicit decoder wiring, ready for the existing Decode action");
        model.SetMode(1); model.Pick(9,40,0); model.Pick(4,80,0); var a=model.A;var b=model.B;
        check(a is {Channel:9,Sample:40,IsEdge:true} && b is {Channel:4,Sample:80,IsEdge:true} && model.Us(40)==20
            && raw.MeasureAt(31,105) is {WidthSamples:10,PeriodSamples:20},
            "Demo waveform supports independent IRQ-to-START delay of 20 us and a 100 kHz SCL cycle");
        acquisition.DecodeDemoCommand.Execute(null);
        await (decodeTask ?? throw new Exception("Decode command did not invoke the shared decoder.")).WaitAsync(TimeSpan.FromSeconds(60));
        var decoded=model.Capture!;
        check(!ReferenceEquals(raw,decoded) && decoded.Signals.SequenceEqual(raw.Signals) && model.A==a && model.B==b && decoded.Packets.Count==4,
            "Shared native I2C workflow decodes two Demo register-read cycles without changing raw waveforms or cross-channel cursors");
        for(int cycle=0;cycle<2;cycle++)
        {
            var write=decoded.Packets[cycle*2];var read=decoded.Packets[cycle*2+1];long offset=cycle*2560;
            check(write.Address=="0x2A" && write.Bytes.SequenceEqual(new byte[]{0x10})
                && write.I2c is {Read:false,Completion:I2cCompletion.RepeatedStart}
                && write.I2c.Acknowledged.SequenceEqual(new bool?[]{true,true})
                && write.Span==new SourceSpan(offset+80,offset+470),
                $"Native Demo cycle {cycle+1}: observed write 0x2A / 0x10, ACKs and repeated START have independent exact timing");
            check(read.Address=="0x2A" && read.Bytes.SequenceEqual(new byte[]{0x5A,0xC3})
                && read.I2c is {Read:true,Completion:I2cCompletion.Stop}
                && read.I2c.Acknowledged.SequenceEqual(new bool?[]{true,true,false}) && read.Diagnostics.Count==0
                && read.Span==new SourceSpan(offset+470,offset+1040),
                $"Native Demo cycle {cycle+1}: observed read 5A C3 ends with NACK and STOP without a false error");
        }
        using var files=new TestWorkspace();string path=Path.Combine(files.Root,"i2c-demo.dsview2");
        await AnalysisFile.WriteAsync(path,new(AnalysisDocument.FileFormat,AnalysisDocument.CurrentVersion,"Demo · I²C + Interrupt",decoded,model.SnapshotState(new(null,0),31)));
        var reopened=await AnalysisFile.ReadAsync(path);
        check(reopened.Capture.Packets.Select(p=>p.Hex).SequenceEqual(new[]{"10","5A C3","10","5A C3"})
            && reopened.State.A==a && reopened.State.B==b && reopened.Capture.EffectiveDecoderSources.Single().Inputs.SequenceEqual(decoded.EffectiveDecoderSources.Single().Inputs),
            "Decoded Demo bytes, explicit wiring and cross-channel measurement survive save/reopen");

        await using var shortSession=new AcquisitionSession(new DemoI2cSource(2_000_000,900,5,31,4,9));
        var shortResult=await shortSession.RunAsync();
        var shortRaw=shortResult.RawCapture!;
        var partial=await NativeI2cSource.DecodeMappedAsync(shortRaw,new(31,4),options,decoderId:"demo-i2c");
        var unfinished=partial.Packets.Last();
        check(shortResult.Termination==AcquisitionTermination.Completed && partial.Packets.Count==2 && unfinished.Bytes.SequenceEqual(new byte[]{0x5A})
            && unfinished.I2c is {Read:true,Completion:I2cCompletion.CaptureEnded}
            && unfinished.I2c.Acknowledged.SequenceEqual(new bool?[]{true,true})
            && unfinished.Diagnostics.Any(d=>d.Code=="incomplete") && unfinished.Annotations!.All(i=>i.Label is not ("STOP" or "NACK")),
            "Capture ending inside C3 keeps only observed 5A and marks incomplete; no missing byte, NACK or STOP is fabricated");

        await using var remappedSession=new AcquisitionSession(new DemoI2cSource(1_000_000,1024,2,0,1,8));
        var remapped=(await remappedSession.RunAsync()).RawCapture!;
        var other=await NativeI2cSource.DecodeMappedAsync(remapped,new(0,1),options,decoderId:"demo-i2c");
        check(other.Packets.Count==2 && other.Packets[0].Span==new SourceSpan(32,188)
            && other.Packets[1].Span==new SourceSpan(188,416) && other.Packets[1].Hex=="5A C3",
            "Changed sample rate, clock and physical pins produce new exact decode timestamps with the same known data");
    }
}
