// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
namespace DSView.Domain;

public sealed record DecoderInput(string Role, int Channel);
public sealed record DecoderSource(string Id, DecoderInput[] Inputs)
{
    public const int MaximumCount = 32;
    public string? Protocol { get; init; }
    public SpiOptions? Spi { get; init; }
    public UartOptions? Uart { get; init; }
    public string EffectiveProtocol => Protocol ?? (Id switch { "i2c" => "i2c", "spi" => "spi", "ua" or "ub" => "uart", _ => "" });
    public string DisplayName => Capture.Decoders.FirstOrDefault(d => d.Id == Id)?.Name ?? $"{EffectiveProtocol.ToUpperInvariant()} · {Id}";
    public string[] RoleOrder => EffectiveProtocol switch { "i2c" => ["scl", "sda"], "spi" => ["clk", "mosi", "miso", "cs"], "uart" => ["data"], _ => [] };
    public DecoderSource WithExplicitProtocol() => this with { Protocol = EffectiveProtocol };
    public static bool ValidId(string? id) => !string.IsNullOrEmpty(id) && id.Length <= 64
        && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');
    // Only for legacy snapshots without explicit source metadata. Never infer
    // a new instance's physical wiring or decode options from these defaults.
    public static DecoderSource[] LegacyDefaults() =>
    [
        new("i2c", [new("scl", 0), new("sda", 1)]),
        new("spi", [new("clk", 2), new("mosi", 3), new("miso", 4), new("cs", 5)]),
        new("ua", [new("data", 6)]), new("ub", [new("data", 7)])
    ];

    public static void Validate(IReadOnlyList<DecoderSource> sources, bool legacyOnly = false)
    {
        if (sources.Count > (legacyOnly ? 4 : MaximumCount) || sources.Any(s => s == null) || sources.Select(s => s.Id).Distinct().Count() != sources.Count)
            throw new InvalidDataException("Invalid or duplicate decoder sources.");
        foreach (var source in sources)
        {
            var legacy = LegacyDefaults().FirstOrDefault(s => s.Id == source.Id);
            if (!ValidId(source.Id)
                || source.Inputs == null || source.Inputs.Length is < 1 or > 4
                || source.Inputs.Any(i => i == null || i.Channel is < 0 or > 31)
                || source.Inputs.Select(i => i.Role).Distinct().Count() != source.Inputs.Length
                || source.Inputs.Select(i => i.Channel).Distinct().Count() != source.Inputs.Length)
                throw new InvalidDataException("Decoder roles must be unique and use distinct physical channels.");
            var roles = source.Inputs.Select(i => i.Role).ToHashSet(StringComparer.Ordinal);
            bool validRoles = source.EffectiveProtocol switch
            {
                "i2c" => roles.SetEquals(["scl", "sda"]),
                "uart" => roles.SetEquals(["data"]),
                "spi" => roles.Contains("clk") && (roles.Contains("mosi") || roles.Contains("miso")) && roles.IsSubsetOf(["clk", "mosi", "miso", "cs"]),
                _ => false
            };
            if (!validRoles || (legacy != null && source.EffectiveProtocol != legacy.EffectiveProtocol)
                || (source.Spi != null && (source.EffectiveProtocol != "spi" || !source.Spi.IsValid))
                || (source.Uart != null && (source.EffectiveProtocol != "uart" || !source.Uart.IsValid)))
                throw new InvalidDataException("Invalid decoder protocol, roles or options.");
            if (legacyOnly && (legacy == null || source.Protocol != null || source.Spi != null || source.Uart != null
                || !roles.SetEquals(legacy.Inputs.Select(i => i.Role))))
                throw new InvalidDataException("Decoder instances, options and optional roles require analysis version 5.");
        }
    }
}
