// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
namespace DSView.Domain;

public sealed record DecoderInput(string Role, int Channel);
public sealed record DecoderSource(string Id, DecoderInput[] Inputs)
{
    // The four existing decoder slots. Additional instances/stacks are a later
    // schema extension; these names describe roles, not physical channel order.
    public static DecoderSource[] LegacyDefaults() =>
    [
        new("i2c", [new("scl", 0), new("sda", 1)]),
        new("spi", [new("clk", 2), new("mosi", 3), new("miso", 4), new("cs", 5)]),
        new("ua", [new("data", 6)]), new("ub", [new("data", 7)])
    ];

    public static void Validate(IReadOnlyList<DecoderSource> sources)
    {
        if (sources.Count > 4 || sources.Any(s => s == null) || sources.Select(s => s.Id).Distinct().Count() != sources.Count)
            throw new InvalidDataException("Invalid or duplicate decoder sources.");
        foreach (var source in sources)
        {
            var expected = LegacyDefaults().FirstOrDefault(s => s.Id == source.Id);
            if (expected == null || source.Inputs == null || source.Inputs.Length != expected.Inputs.Length
                || source.Inputs.Any(i => i == null || i.Channel is < 0 or > 31)
                || source.Inputs.Select(i => i.Role).Distinct().Count() != source.Inputs.Length
                || !source.Inputs.Select(i => i.Role).ToHashSet(StringComparer.Ordinal).SetEquals(expected.Inputs.Select(i => i.Role))
                || source.Inputs.Select(i => i.Channel).Distinct().Count() != source.Inputs.Length)
                throw new InvalidDataException("Decoder roles must be complete, unique and use distinct physical channels.");
        }
    }
}
