// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using DSView.Domain;
using DSView.Infrastructure;

internal static class AcquisitionPreviewChecks
{
    private sealed class Source(AcquisitionPlan plan,Func<AcquisitionWriter,CancellationToken,Task<AcquisitionTermination>> run) : IAcquisitionSource
    {
        public AcquisitionPlan Plan=>plan;
        public Task<AcquisitionTermination> AcquireAsync(AcquisitionWriter writer,CancellationToken stop,CancellationToken abort)=>run(writer,abort);
    }
    public static async Task RunAsync(Action<bool,string> check)
    {
        var plan=new AcquisitionPlan(AcquisitionSourceKind.Demo,"Preview checks",1000,20,[new(0,"Clock"),new(31,"Control")]);
        var buffer=new AcquisitionBuffer(plan,100);var operation=Guid.NewGuid();
        check(buffer.Preview(operation,new())==null,"No preview is invented before any sample is retained");
        buffer.Append(new(0,[0,1,0,1,0,0x80000001,0x80000000,0x80000001,0x80000000,0x80000001]));
        var first=buffer.Preview(operation,new(WindowSamples:8,EdgesPerChannel:3))!;
        check(first.Operation==operation && first is {SampleRate:1000,StartSample:7,EndSample:10,WindowReduced:true}
            && first.Signals[0].Edges.SequenceEqual(new Edge[]{new(7,true),new(8,false),new(9,true)})
            && !first.Signals[0].Initial && first.Signals[1] is {Id:31,Initial:true,Edges.Count:0},
            "Dense preview reduces all channels to one common absolute window and keeps correct boundary levels");
        var oldEdges=first.Signals[0].Edges.ToArray();
        buffer.Append(new(10,[0x80000001,0x80000001,0x80000001,0x80000000,0x80000000,0x80000000,0x80000000,0x80000000,0x80000000,0x80000000]));
        var recent=buffer.Preview(operation,new(WindowSamples:5,EdgesPerChannel:3))!;
        check(recent is {StartSample:15,EndSample:20,WindowReduced:false} && recent.Signals.All(s=>s.Edges.Count==0)
            && !recent.Signals[0].Initial && recent.Signals[1].Initial,
            "A recent quiet window retains true levels without borrowing edges from before its coverage");
        check(first.EndSample==10 && first.Signals[0].Edges.SequenceEqual(oldEdges),"Later acquisition cannot mutate an already published preview");
        bool immutable=false;try {((IList<Edge>)first.Signals[0].Edges)[0]=new(7,false);}catch(NotSupportedException){immutable=true;}
        check(immutable,"Preview edge collections reject external mutation");
        var final=buffer.Snapshot(AcquisitionTermination.Completed)!;
        check(final.SampleCount==20 && final.Signals[0].Edges.Count==10 && final.Signals[0].Edges[0].Sample==1,
            "Reducing preview coverage does not truncate the retained final capture");
        buffer.Release();check(first.Signals[0].Edges.SequenceEqual(oldEdges),"Preview survives release of mutable acquisition storage");

        var source=new Source(plan,(writer,_)=>
        {
            writer.Running();writer.TryWrite(0,[0,1,0,1,0,1,0,1,0,1]);writer.TryWrite(10,new uint[10]);
            return Task.FromResult(AcquisitionTermination.Completed);
        });
        await using(var session=new AcquisitionSession(source))
        {
            session.RequestPreview(new(10,9));session.RequestPreview(new(4,2));
            var result=await session.RunAsync().WaitAsync(TimeSpan.FromSeconds(10));var preview=session.LatestPreview!;
            check(result.CanPublish && preview.Operation==session.Operation && preview is {StartSample:8,EndSample:10}
                && preview.Signals[0].Edges.Count==2 && result.RawCapture!.SampleCount==20,
                "Coalesced preview request uses newest bounds and never publishes a mutable or full-capture substitute");
            check(!session.RequestPreview(),"A finished session rejects further preview work");
            session.Cancel();check(session.LatestPreview==null,"Explicit cancellation clears previously available provisional data");
        }
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelling=new Source(plan,async(writer,token)=>
        {
            writer.Running();writer.TryWrite(0,[0,1,0,1,0,1,0,1,0,1]);entered.SetResult();
            await Task.Delay(Timeout.Infinite,token);return AcquisitionTermination.Completed;
        });
        await using(var session=new AcquisitionSession(cancelling))
        {
            session.RequestPreview();var running=session.RunAsync();await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));session.Cancel();
            var result=await running.WaitAsync(TimeSpan.FromSeconds(10));
            check(result.Termination==AcquisitionTermination.Cancelled && session.LatestPreview==null && !session.RequestPreview(),
                "Cancellation racing queued samples cannot republish a late preview");
        }
        var disposed=new AcquisitionSession(source);disposed.RequestPreview();await disposed.RunAsync();await disposed.DisposeAsync();
        check(disposed.LatestPreview==null && !disposed.RequestPreview(),"Disposal releases preview references and closes the preview request boundary");
        await using(var limited=new AcquisitionSession(source,new(Transitions:2)))
        {
            limited.RequestPreview();var result=await limited.RunAsync();
            check(result.Termination==AcquisitionTermination.Overflow && result.AcceptedSamples>result.RetainedSamples
                && result.RetainedSamples==3 && limited.LatestPreview is {StartSample:0,EndSample:3}
                && limited.LatestPreview.Signals[0].Edges.Count==2,
                "Overflow preview ends at the retained prefix, never at samples only admitted to the queue");
        }
        foreach(var invalid in new[]{new AcquisitionPreviewOptions(0,1),new(1,0),new(1_000_001,1),new(1,8193)})
        {
            await using var session=new AcquisitionSession(source);bool rejected=false;
            try{session.RequestPreview(invalid);}catch(ArgumentOutOfRangeException){rejected=true;}
            check(rejected,"Preview request rejects an out-of-bounds sample window or edge budget");
        }
    }
}
