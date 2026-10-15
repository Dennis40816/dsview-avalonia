// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using DSView.Desktop;
using DSView.Domain;

internal static class ChannelActivityCacheChecks
{
    public static void Run(Action<bool,string> check)
    {
        var edges=new CountedEdges(Enumerable.Range(1,100_000).Select(i=>new Edge(i*10L,(i&1)==1)).ToArray());
        var signal=new Signal(7,"Clock",false,edges);
        var raw=new Capture(1_000_000,1_000_010,[signal,new(31,"Reset",true,[])],[]) {DecoderSources=[]};
        IReadOnlyList<Signal> visible=raw.Signals;
        var cache=new ChannelActivityCache();
        cache.Update(raw,visible,0,raw.SampleCount);
        check(cache.Items[0] is {Channel:7,Short:"100000 edges"} && cache.Items[1] is {Channel:31,Short:"High · 0 edges"},
            "Activity presentation retains source-derived transition count and known idle level");
        var labels=cache.Items;edges.Reset();bool changed=false;
        long allocation=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<1000;i++) changed|=cache.Update(raw,visible,0,raw.SampleCount);
        allocation=GC.GetAllocatedBytesForCurrentThread()-allocation;
        check(!changed && ReferenceEquals(labels,cache.Items) && edges.Reads==0 && edges.Enumerated==0 && allocation==0,
            "1000 unchanged-view updates reuse labels without allocations or reading a 100000-edge source");
        cache.Update(raw,visible,10.1,20.1);
        check(cache.Items[0].Short=="1 edges", "Fractional viewport changes use the existing half-open sample coverage");
        edges.Reset();labels=cache.Items;
        check(!cache.Update(raw,visible,10.9,20.9) && ReferenceEquals(labels,cache.Items) && edges.Reads==0,
            "Sub-sample movement within identical observed sample bounds reuses activity evidence");
        cache.Update(raw,visible,21,29);
        check(cache.Items[0].Short=="Low · 0 edges", "Zoom into an idle region refreshes its exact known level");
        var gapped=raw with {Gaps=[new(15,25)]};cache.Update(gapped,visible,21,29);
        check(cache.Items[0].Short=="Incomplete", "Source replacement with missing coverage invalidates cached certainty");
        cache.Update(raw,[raw.Signals[1]],0,raw.SampleCount);
        check(cache.Items.Count==1 && cache.Items[0].Channel==31, "Group switch removes unrelated channel labels");
        cache.Update(raw,[],0,raw.SampleCount);
        check(cache.Items.Count==0, "An empty group does not retain labels from the previous group");
        cache.Update(null,[],0,raw.SampleCount);
        var changedSignal=raw with {Signals=[new(7,"Replacement",true,[])]};cache.Update(changedSignal,changedSignal.Signals,0,raw.SampleCount);
        check(cache.Items.Single().Short=="High · 0 edges", "Clearing then replacing capture never displays stale channel activity");
    }
}
