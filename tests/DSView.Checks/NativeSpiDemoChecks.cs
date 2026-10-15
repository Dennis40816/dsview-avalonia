// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using DSView.Checks.Support;
using DSView.Desktop;
using DSView.Domain;
using DSView.Infrastructure;

internal static class NativeSpiDemoChecks
{
    public static async Task RunAsync(Action<bool,string> check, string configuration)
    {
        var runtime=NativeSerialSource.ReadConfiguration(configuration);
        var model=new Workbench();Task? decoding=null;
        using var decoder=new DecoderViewModel(model,async(raw,binding,token)=>await NativeSerialSource.DecodeAsync(raw,binding,runtime,token));
        await using var acquisition=new AcquisitionViewModel(model,()=>!decoder.IsRunning,()=>Task.FromResult(true),_=>{},decodeRequested:id=>
        {
            decoder.Open();decoder.Target=decoder.Targets.Single(t=>t.Source?.Id==id);
            decoding=decoder.DecodeCommand.ExecuteAsync(null);
        });
        var draft=acquisition.Draft;
        draft.Scenario="SPI · Mode 0";draft.Channel="31";draft.DataChannel="4";draft.MisoChannel="9";draft.ChipSelectChannel="12";
        draft.Rate="1000000";draft.Frequency="100000";draft.Duration="1.28";
        await acquisition.StartCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(10));
        var raw=model.Capture!;var binding=raw.EffectiveDecoderSources.Single();
        check(raw is {SampleRate:1_000_000,SampleCount:1280,Packets.Count:0} && raw.Signals.Select(s=>s.Id).SequenceEqual(new[]{4,9,12,31})
            && binding.SameConfiguration(new("demo-spi",[new("clk",31),new("mosi",4),new("miso",9),new("cs",12)]) {Protocol="spi",Spi=new(0,"msb-first",8,"active-low")})
            && acquisition.DecodeLabel=="Decode SPI" && acquisition.DecodeDemoCommand.CanExecute(null),
            "SPI Demo publishes raw four-wire samples and explicit mode-0 decoder mapping");
        model.SetMode(1);model.Pick(12,40,0);model.Pick(31,55,0);var a=model.A;var b=model.B;
        check(a is {Channel:12,Sample:40,IsEdge:true} && b is {Channel:31,Sample:55,IsEdge:true}
            && model.Us(15)==15 && raw.MeasureAt(31,56) is {WidthSamples:5,PeriodSamples:10},
            "SPI Demo supports 15 us CS-to-first-edge measurement and a complete 100 kHz clock period");
        acquisition.DecodeDemoCommand.Execute(null);
        await (decoding??throw new Exception("SPI decode not requested")).WaitAsync(TimeSpan.FromSeconds(60));
        var capture=model.Capture!;
        check(capture.Packets.Count==2 && capture.Packets.All(p=>p.Spi is {Completion:SpiCompletion.ChipSelectReleased}
            && p.Spi.Words.Select(w=>w.Mosi?.Hex).SequenceEqual(new[]{"9F","00","A5"})
            && p.Spi.Words.Select(w=>w.Miso?.Hex).SequenceEqual(new[]{"00","EF","17"})),
            "Real native SPI decodes both full-duplex Demo bursts and observed CS release");
        check(capture.Packets.Select(p=>p.Span).SequenceEqual(new[]{new SourceSpan(40,300),new SourceSpan(680,940)})
            && capture.Signals.SequenceEqual(raw.Signals) && model.A==a && model.B==b,
            "SPI transfers retain exact CS intervals, original waveforms and cross-channel cursors");
        var packet=capture.Packets[0];var lane=model.DecoderLanes.Single(l=>l.Direction=="MISO");
        model.SelectProtocol(packet,lane.Items(capture,packet).ElementAt(1).Index);
        check(model.SelectedProtocol?.Serial is {Direction:"MISO",Word.Hex:"EF"},
            "SPI Demo MISO selection inspects received data independently of MOSI");
        using var files=new TestWorkspace();string path=Path.Combine(files.Root,"spi-demo.dsview2");
        await AnalysisFile.WriteAsync(path,new(AnalysisDocument.FileFormat,AnalysisDocument.CurrentVersion,"SPI Demo",capture,model.SnapshotState(new(null,0),31)));
        model.RestoreAnalysis(await AnalysisFile.ReadAsync(path));
        check(model.SelectedProtocol?.Serial is {Direction:"MISO",Word.Hex:"EF"} && model.A==a && model.B==b
            && model.Capture!.EffectiveDecoderSources.Single().SameConfiguration(binding),
            "SPI Demo save/reopen preserves MISO word selection, all roles and timing cursors");
        await using var shortSession=new AcquisitionSession(new DemoSpiSource(1_000_000,180,5,31,4,9,12));
        var shortResult=await shortSession.RunAsync();var partial=await NativeSerialSource.DecodeAsync(shortResult.RawCapture!,binding,runtime);
        check(shortResult.Termination==AcquisitionTermination.Completed && partial.Packets.Single().Spi is {Completion:SpiCompletion.CaptureEnded,Words.Length:1}
            && partial.Packets.Single().Spi!.Words[0] is {Mosi.Hex:"9F",Miso.Hex:"00"}
            && partial.Packets.Single().Annotations!.All(x=>x.Label!="CS released"),
            "SPI capture ending inside the second word retains only observed words and does not fabricate CS release");
        await using var otherSession=new AcquisitionSession(new DemoSpiSource(600_000,384,3,0,1,2,3));
        var otherRaw=(await otherSession.RunAsync()).RawCapture!;
        var other=await NativeSerialSource.DecodeAsync(otherRaw,binding with {Inputs=[new("clk",0),new("mosi",1),new("miso",2),new("cs",3)]},runtime);
        check(other.Packets.Single().Span==new SourceSpan(24,180) && other.Packets.Single().Spi!.Words.Select(w=>w.Miso!.Hex).SequenceEqual(new[]{"00","EF","17"}),
            "Changed SPI sample rate, half-period and pins preserve independent CS timing and received bytes");
    }
}
