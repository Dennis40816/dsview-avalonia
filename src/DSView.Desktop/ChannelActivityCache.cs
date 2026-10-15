// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using DSView.Domain;

namespace DSView.Desktop;

internal sealed record ChannelActivityLabel(int Channel, string Short, string Full);

// UI-thread presentation cache for one immutable capture and visible group/range.
// Cursor, selection and appearance changes do not change raw activity evidence.
internal sealed class ChannelActivityCache
{
    private Capture? capture;
    private IReadOnlyList<Signal>? signals;
    private long start, end;
    public IReadOnlyList<ChannelActivityLabel> Items { get; private set; } = [];
    public bool Update(Capture? source, IReadOnlyList<Signal> visible, double viewStart, double viewEnd)
    {
        long first=(long)Math.Ceiling(viewStart), last=(long)Math.Ceiling(viewEnd);
        if(ReferenceEquals(capture,source) && ReferenceEquals(signals,visible) && start==first && end==last) return false;
        var items=new ChannelActivityLabel[source==null ? 0 : visible.Count];
        for(int i=0;i<items.Length;i++)
        {
            var activity=source!.Activity(visible[i].Id,first,last);
            items[i]=new(visible[i].Id,ActivityText.Short(activity),ActivityText.Full(activity));
        }
        capture=source; signals=source==null ? null : visible; start=first;end=last;
        Items=Array.AsReadOnly(items);return true;
    }
}
