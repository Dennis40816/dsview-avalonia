// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using DSView.Checks.Support;
using DSView.Desktop;
using DSView.Domain;
using DSView.Infrastructure;

internal static class NativeUartDemoChecks
{
    public static async Task RunAsync(Action<bool,string> check, string configuration)
    {
        var runtime=NativeSerialSource.ReadConfiguration(configuration);
        var model=new Workbench(); Task? decoding=null;
        using var decoder=new DecoderViewModel(model,async(raw,binding,token)=>await NativeSerialSource.DecodeAsync(raw,binding,runtime,token));
        await using var acquisition=new AcquisitionViewModel(model,()=>!decoder.IsRunning,()=>Task.FromResult(true),_=>{},decodeRequested:id=>
        {
            decoder.Open(); decoder.Target=decoder.Targets.Single(t=>t.Source?.Id==id);
            decoding=decoder.DecodeCommand.ExecuteAsync(null);
        });
        var draft=acquisition.Draft;
        draft.Scenario="UART · 8N1"; draft.Channel="31"; draft.Rate="1000000"; draft.Frequency="100000"; draft.Duration="1.28";
        await acquisition.StartCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(10));
        var raw=model.Capture!;
        check(raw is {SampleRate:1_000_000,SampleCount:1280,Packets.Count:0} && raw.Signals.Single().Id==31
            && raw.EffectiveDecoderSources.Single() is {Id:"demo-uart",Uart.BaudRate:100000,Uart.DataBits:8,Uart.Parity:"none",Uart.StopBits:1}
            && acquisition.DecodeLabel=="Decode UART" && acquisition.DecodeDemoCommand.CanExecute(null),
            "UART Demo acquisition publishes raw TX and exact 8N1 decoder configuration through the shared owner");
        model.SetMode(1); model.Pick(31,80,0); model.Pick(31,90,0); var a=model.A;var b=model.B;
        draft.Scenario="Clock";
        check(acquisition.DecodeLabel=="Decode UART" && model.Us(b!.Sample-a!.Sample)==10,
            "Decode action follows the acquired source, not next-capture draft; UART start width measures 10 us");
        acquisition.DecodeDemoCommand.Execute(null);
        await (decoding??throw new Exception("UART decode not requested")).WaitAsync(TimeSpan.FromSeconds(60));
        var capture=model.Capture!;
        check(capture.Packets.Select(p=>p.Uart?.Data?.Hex).SequenceEqual(new[]{"55","A5","00","FF","55","A5","00","FF"})
            && capture.Packets.All(p=>p.Uart is {Completion:UartCompletion.StopObserved,Valid:true} && p.Diagnostics.Count==0),
            "Real native UART decodes two back-to-back 8N1 bursts as 55 A5 00 FF without false errors");
        check(capture.Packets.Select(p=>p.Span.Start).SequenceEqual(new long[]{80,180,280,380,720,820,920,1020})
            && capture.Signals.SequenceEqual(raw.Signals) && model.A==a && model.B==b,
            "UART frame starts match independent sample positions while raw waveforms and A/B stay unchanged");
        using var files=new TestWorkspace(); string path=Path.Combine(files.Root,"uart-demo.dsview2");
        await AnalysisFile.WriteAsync(path,new(AnalysisDocument.FileFormat,AnalysisDocument.CurrentVersion,"UART Demo",capture,model.SnapshotState(new(null,0),31)));
        var reopened=await AnalysisFile.ReadAsync(path);
        check(reopened.Capture.Packets.Select(p=>p.Uart?.Data?.Hex).SequenceEqual(capture.Packets.Select(p=>p.Uart?.Data?.Hex))
            && reopened.Capture.EffectiveDecoderSources.Single().SameConfiguration(capture.EffectiveDecoderSources.Single())
            && reopened.State.A==a && reopened.State.B==b,
            "UART Demo save/reopen retains decoded words, baud, physical channel and cursor positions");
        await using var shortSession=new AcquisitionSession(new DemoUartSource(1_000_000,172,100000,31));
        var shortResult=await shortSession.RunAsync();
        var partial=await NativeSerialSource.DecodeAsync(shortResult.RawCapture!,raw.EffectiveDecoderSources.Single(),runtime);
        check(shortResult.Termination==AcquisitionTermination.Completed && partial.Packets.Single().Uart is {Data.Hex:"55",Completion:UartCompletion.Incomplete,Valid:null}
            && partial.Packets.Single().Annotations!.All(x=>x.Label!="Stop"),
            "Capture ending before UART stop-bit center preserves observed data without claiming a validated frame");
        await using var remappedSession=new AcquisitionSession(new DemoUartSource(1_036_800,576,115200,7));
        var remapped=(await remappedSession.RunAsync()).RawCapture!;
        var binding=raw.EffectiveDecoderSources.Single() with {Inputs=[new("data",7)],Uart=new(115200,8,"none",1,"lsb-first",false)};
        var other=await NativeSerialSource.DecodeAsync(remapped,binding,runtime);
        check(other.Packets.Select(p=>p.Uart?.Data?.Hex).SequenceEqual(new[]{"55","A5","00","FF"})
            && other.Packets.Select(p=>p.Span.Start).SequenceEqual(new long[]{72,162,252,342}),
            "115200-baud UART at nine samples per bit and remapped D7 preserves independent timing and bytes");
    }
}
