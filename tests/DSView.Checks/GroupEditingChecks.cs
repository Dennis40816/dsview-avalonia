// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Json;
using DSView.Desktop;
using DSView.Domain;

internal static class GroupEditingChecks
{
    public static void Run(Action<bool, string> check, Capture board)
    {
        var twoChannels = board with { Signals = board.Signals.Where(s => s.Id is 0 or 1).ToArray() };
        var original = new ObservationGroup("I²C + Control", [0, 1, 8], ["i2c"]);
        var draft = new GroupEditState(twoChannels, original);
        check(draft.TryBuild(original.Name, [original], original, out var unchanged, out _)
            && unchanged!.Channels.SequenceEqual(original.Channels) && unchanged.Decoders.SequenceEqual(original.Decoders)
            && draft.OfferedChannels.SequenceEqual(new[] { 0, 1, 8 }), "Editing a two-channel capture preserves an existing absent control signal without offering unrelated absent channels");
        draft.SetChannel(8, false); draft.SetChannel(8, true);
        check(draft.Channels.Contains(8) && draft.OfferedChannels.Contains(8) && original.Channels.SequenceEqual(new[] { 0, 1, 8 }), "An unavailable saved member remains reversible within a detached draft");

        var unavailable = new GroupEditState(twoChannels, new("Saved SPI", [2, 3, 4, 5], ["spi"]));
        var spi = unavailable.OfferedDecoders.Single(d => d.Id == "spi");
        unavailable.SetChannel(3, false);
        check(!unavailable.HasSources(spi) && !unavailable.Decoders.Contains("spi") && new[] { 2, 4, 5 }.All(unavailable.Channels.Contains), "Removing a missing source disables its saved decoder while preserving the other raw members");
        unavailable.SetDecoder("spi", true);
        check(unavailable.Decoders.Contains("spi") && spi.Channels.All(unavailable.Channels.Contains), "A removed unavailable decoder remains offered so its original membership can be restored before saving");

        var fresh = new GroupEditState(twoChannels, null); fresh.SetDecoder("i2c", true); fresh.SetDecoder("i2c", false);
        check(fresh.Channels.SetEquals([0, 1]) && fresh.Decoders.Count == 0, "Removing a decoder leaves its captured source signals in the draft");
        fresh.SetDecoder("i2c", true); fresh.SetChannel(1, false); fresh.SetChannel(1, true);
        check(fresh.Decoders.Count == 0 && fresh.Channels.SetEquals([0, 1]), "Re-adding a raw signal does not silently reactivate its decoder");
        fresh.SetChannel(8, true); fresh.SetDecoder("spi", true);
        check(fresh.Channels.SetEquals([0, 1]) && fresh.OfferedDecoders.Select(d => d.Id).SequenceEqual(new[] { "i2c" }), "New Group choices cannot imply uncaptured control or SPI sources are available");

        var all = new ObservationGroup("All signals", Enumerable.Range(0, 32).ToArray(), []);
        var copy = new GroupEditState(twoChannels, all);
        check(copy.Channels.SetEquals([0, 1]) && all.Channels.Length == 32 && copy.TryBuild("Raw pair", [all], null, out _, out _), "Copying All signals starts with current captured IDs and leaves the reserved aggregate unchanged");
        var mixed = new GroupEditState(board, null); mixed.SetDecoder("spi", true); mixed.SetDecoder("ua", true); mixed.SetChannel(8, true);
        check(mixed.TryBuild("Bus + IRQ", [all], null, out var combination, out _) && combination!.Channels.SequenceEqual(new[] { 2, 3, 4, 5, 6, 8 }) && combination.Decoders.Order().SequenceEqual(new[] { "spi", "ua" }), "A Group can combine SPI, UART TX and an independent control signal");
        check(!copy.TryBuild("all SIGNALS", [all], all, out _, out _) && !copy.TryBuild("I²C + CONTROL", [original], null, out _, out _) && copy.Channels.SetEquals([0, 1]), "Reserved and duplicate Group names are case-insensitive and validation preserves the draft");
        check(!new GroupEditState(board with { Signals = [] }, all).TryBuild("Empty copy", [all], null, out _, out _), "An empty capture cannot create an empty Group by copying All signals");

        var saved = Enumerable.Range(1, 25).Select(i => new ObservationGroup($"Group {i}", [0, 1, 8], ["i2c"])).ToArray();
        var reopened = GroupEditState.Restore(JsonSerializer.Deserialize<ObservationGroup[]>(JsonSerializer.Serialize(saved)));
        check(reopened.Length == 25 && reopened.Select(g => g.Name).SequenceEqual(saved.Select(g => g.Name))
            && reopened.Single(g => g.Name == "Group 25").Channels.SequenceEqual(new[] { 0, 1, 8 }), "All 25 valid saved Groups survive serialization and the actual restore normalizer, including the last active candidate");
        check(GroupEditState.Restore([new("One", [8], ["i2c"])]).Single().Channels.SequenceEqual(new[] { 0, 1, 8 })
            && GroupEditState.Restore([null!, new("ALL SIGNALS", [0], []), new("Bad", [32], [])]).Length == 0, "Preference restoration preserves decoder source invariants and rejects malformed or reserved aggregate entries");
    }
}
