// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using DSView.Domain;

namespace DSView.Desktop;

// A detached draft keeps Cancel and unavailable saved members reversible.
public sealed class GroupEditState
{
    public IReadOnlySet<int> CapturedChannels { get; }
    public IReadOnlyList<int> OfferedChannels { get; }
    public IReadOnlyList<Decoder> OfferedDecoders { get; }
    public HashSet<int> Channels { get; }
    public HashSet<string> Decoders { get; }

    public GroupEditState(Capture? capture, ObservationGroup? original)
    {
        CapturedChannels = (capture?.Signals.Select(s => s.Id) ?? []).ToHashSet();
        bool aggregate = original?.Name == "All signals";
        Channels = aggregate ? CapturedChannels.ToHashSet() : (original?.Channels ?? []).ToHashSet();
        Decoders = aggregate ? [] : (original?.Decoders ?? []).ToHashSet();
        OfferedDecoders = (capture?.ResolvedDecoders ?? Capture.Decoders).Where(d => d.Channels.All(CapturedChannels.Contains) || Decoders.Contains(d.Id)).ToArray();
        foreach (var decoder in OfferedDecoders.Where(d => Decoders.Contains(d.Id))) Channels.UnionWith(decoder.Channels);
        OfferedChannels = CapturedChannels.Concat(Channels).Distinct().Order().ToArray();
    }

    public bool HasSources(Decoder decoder) => decoder.Channels.All(CapturedChannels.Contains);

    public void SetChannel(int channel, bool included)
    {
        if (!OfferedChannels.Contains(channel)) return;
        if (included) Channels.Add(channel);
        else
        {
            Channels.Remove(channel);
            foreach (var decoder in OfferedDecoders.Where(d => d.Channels.Contains(channel))) Decoders.Remove(decoder.Id);
        }
    }

    public void SetDecoder(string id, bool included)
    {
        var decoder = OfferedDecoders.FirstOrDefault(d => d.Id == id);
        if (decoder == null) return;
        if (included) { Decoders.Add(id); Channels.UnionWith(decoder.Channels); }
        else Decoders.Remove(id);
    }

    public bool TryBuild(string? name, IEnumerable<ObservationGroup> existing, ObservationGroup? replaced,
        out ObservationGroup? result, out string error)
    {
        result = null; string title = name?.Trim() ?? "";
        if (title.Length is 0 or > 40) { error = "Enter a group name (up to 40 characters)."; return false; }
        if (title.Equals("All signals", StringComparison.OrdinalIgnoreCase)) { error = "All signals is reserved. Choose a different name."; return false; }
        if (existing.Any(g => !ReferenceEquals(g, replaced) && g.Name.Equals(title, StringComparison.OrdinalIgnoreCase))) { error = "Choose a different group name."; return false; }
        if (Channels.Count == 0) { error = "Select at least one signal."; return false; }
        result = new(title, Channels.Order().ToArray(), Decoders.Order().ToArray()); error = ""; return true;
    }

    public static ObservationGroup[] Restore(IEnumerable<ObservationGroup>? saved) => (saved ?? [])
        .Where(g => g != null && !string.IsNullOrWhiteSpace(g.Name) && g.Name.Length <= 40
            && !g.Name.Equals("All signals", StringComparison.OrdinalIgnoreCase)
            && g.Channels is { Length: > 0 } && g.Channels.All(c => c is >= 0 and < 32) && g.Decoders != null)
        .Select(g =>
        {
            var decoders = g.Decoders.Where(DecoderSource.ValidId).Distinct().Take(DecoderSource.MaximumCount).ToArray();
            // Preferences are reusable memberships, not evidence of a capture's
            // decoder wiring. Do not inject the demo's D0/D1 into mapped Groups.
            return new ObservationGroup(g.Name, g.Channels.Distinct().Order().ToArray(), decoders);
        }).ToArray();
}
