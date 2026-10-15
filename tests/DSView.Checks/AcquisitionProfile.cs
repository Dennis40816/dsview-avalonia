// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Diagnostics;
using System.Text.Json;
using DSView.Desktop;
using DSView.Domain;
using DSView.Infrastructure;

// Explicit diagnostic, not a timing-sensitive test. No USB, UI drawing or disk IO.
internal static class AcquisitionProfile
{
    private sealed record Stage(string Name, double Milliseconds, long AllocatedBytes);
    private static T Measure<T>(string name, Func<T> action, List<Stage> stages)
    {
        long allocation=GC.GetAllocatedBytesForCurrentThread(), start=Stopwatch.GetTimestamp();
        T result=action();
        stages.Add(new(name,Stopwatch.GetElapsedTime(start).TotalMilliseconds,GC.GetAllocatedBytesForCurrentThread()-allocation));
        return result;
    }
    public static async Task RunAsync()
    {
        Run(65_536,1,64,0); // Warm relevant paths; excluded from reports.
        foreach (var item in new[]{(262_144,1,64),(3_932_160,1,64),(3_932_160,1,2),(3_932_160,32,64)})
            for(int repeat=1;repeat<=3;repeat++) Run(item.Item1,item.Item2,item.Item3,repeat);
        foreach (IAcquisitionSource source in new IAcquisitionSource[]{
            new DemoClockSource(1_000_000,1_000_000,50),new DemoI2cSource(1_000_000,1_000_000,25),
            new DemoUartSource(1_000_000,1_000_000,10000),new DemoSpiSource(1_000_000,1_000_000,50)})
        {
            long allocated=GC.GetTotalAllocatedBytes(true),start=Stopwatch.GetTimestamp();
            await using var session=new AcquisitionSession(source);
            var result=await session.RunAsync().WaitAsync(TimeSpan.FromSeconds(30));
            if(!result.CanPublish) throw new InvalidOperationException("Profile source did not complete: "+result.Detail);
            Console.WriteLine("ACQUISITION_PACED "+JsonSerializer.Serialize(new {
                scenario=source.Plan.SourceName,result.RetainedSamples,
                edges=result.RawCapture!.Signals.Sum(s=>s.Edges.Count),
                elapsedMs=Stopwatch.GetElapsedTime(start).TotalMilliseconds,
                processAllocatedBytes=GC.GetTotalAllocatedBytes(true)-allocated,
                simulatedSeconds=(double)source.Plan.SampleLimit/source.Plan.SampleRate,
                note="Actual paced Demo session including generation, queue, conversion and final snapshot. Process-wide allocations; no UI or native decode."}));
        }
    }
    private static void Run(int samples,int channels,int halfPeriod,int repeat)
    {
        var plan=new AcquisitionPlan(AcquisitionSourceKind.Demo,"Profile",50_000_000,samples,
            Enumerable.Range(0,channels).Select(id=>new AcquisitionChannel(id,"D"+id)).ToArray());
        var batch=Enumerable.Range(0,4096).Select(i=>(i/halfPeriod&1)==0?0u:plan.ChannelMask).ToArray();
        var writer=new AcquisitionWriter(plan,new()); var buffer=new AcquisitionBuffer(plan,2_000_000);
        var stages=new List<Stage>();
        Measure("ingress-copy-and-transition-conversion",()=>
        {
            writer.Running();
            for(int start=0;start<samples;start+=batch.Length)
                if(!writer.TryWrite(start,batch) || !writer.Reader.TryRead(out var admitted) || !buffer.Append(admitted))
                    throw new InvalidOperationException("Profile ingress failed.");
            writer.Complete();return buffer.Count;
        },stages);
        var preview=Measure("bounded-recent-preview",()=>buffer.Preview(Guid.Empty,new())!,stages);
        if(preview.Signals.Any(s=>s.Edges.Count>2048) || preview.EndSample!=samples || preview.StartSample<0)
            throw new InvalidOperationException("Profile preview exceeded its declared coverage or edge budget.");
        var raw=Measure("immutable-snapshot",()=>buffer.Snapshot(AcquisitionTermination.Completed)!,stages);
        buffer.Release();
        long expected=(samples/(long)halfPeriod-1)*channels;
        if(raw.SampleCount!=samples || raw.Signals.Sum(s=>(long)s.Edges.Count)!=expected) throw new InvalidOperationException("Profile lost samples or edges.");
        var document=Measure("validate-imported-analysis",()=>Workbench.CreateImportedAnalysis(new("Profile",raw),[],[]),stages);
        var prepared=Measure("prepare-analysis",()=>Workbench.PrepareValidatedAnalysis(document),stages);
        var model=new Workbench();
        Measure("apply-model-no-ui-subscribers",()=>{model.ApplyAnalysis(prepared);return model.Signals.Count;},stages);
        Measure("overview-1024-bins",()=>raw.ActivityBins(raw.Signals,1024).Length,stages);
        foreach(int width in new[]{1024,1920})
            Measure("full-view-projection-"+width,()=>raw.Signals.Sum(s=>WaveformProjection.Create(s,0,samples,width).Runs.Count),stages);
        Measure("zoomed-projection-1024",()=>raw.Signals.Sum(s=>WaveformProjection.Create(s,samples/2,samples/2+1000,1024).Runs.Count),stages);
        Measure("64-cursor-measurements",()=>
        {
            long total=0;
            for(int i=0;i<64;i++) total+=raw.MeasureAt(i%channels,1L+i*(samples-2L)/64).WidthSamples??0;
            return total;
        },stages);
        var activity=new ChannelActivityCache();
        Measure("channel-labels-first",()=>activity.Update(raw,raw.Signals,0,samples),stages);
        Measure("100-channel-label-refreshes-cached",()=>
        {
            for(int i=0;i<100;i++) activity.Update(raw,raw.Signals,0,samples);
            return activity.Items.Count;
        },stages);
        Measure("100-channel-label-refreshes-uncached",()=>
        {
            int length=0;
            for(int i=0;i<100;i++) foreach(var signal in raw.Signals)
            {
                var value=raw.Activity(signal.Id,0,samples);
                length+=ActivityText.Short(value).Length+ActivityText.Full(value).Length;
            }
            return length;
        },stages);
        if(repeat>0) Console.WriteLine("ACQUISITION_PROFILE "+JsonSerializer.Serialize(new {
            samples,channels,halfPeriod,edges=expected,repeat,stages,
            note="Pre-generated samples; sequential ingress/consumer to isolate CPU and allocation cost. No pacing, native callback, queue contention, GPU or UI event handlers. Allocations are per stage, not retained memory."}));
    }
}
