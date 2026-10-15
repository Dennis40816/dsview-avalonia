// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DSView.Infrastructure;

public sealed record DisplayPreferences(int Version, double TextSize, double DataTextSize, double WaveformThickness, bool ReverseWheel)
{
    public static DisplayPreferences Default { get; } = new(1, 13, 13, 1.35, false);
    [JsonIgnore]
    public bool IsValid => Version == 1 && double.IsFinite(TextSize) && TextSize is >= 12 and <= 16 &&
        double.IsFinite(DataTextSize) && DataTextSize is >= 12 and <= 18 &&
        double.IsFinite(WaveformThickness) && WaveformThickness is >= 1 and <= 3;

    public static AppearanceLoadResult Admit(string json)
    {
        if (json.Length > 16_384) return AppearanceLoadResult.Invalid;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return AppearanceLoadResult.Invalid;
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in document.RootElement.EnumerateObject())
                if (field.Name is not ("Version" or "TextSize" or "DataTextSize" or "WaveformThickness" or "ReverseWheel") || !names.Add(field.Name))
                    return AppearanceLoadResult.Invalid;
            if (names.Count != 5) return AppearanceLoadResult.Invalid;
            var value = document.RootElement.Deserialize<DisplayPreferences>();
            return value?.IsValid == true ? new(value, null) : AppearanceLoadResult.Invalid;
        }
        catch (JsonException) { return AppearanceLoadResult.Invalid; }
    }
    public string Serialize() => JsonSerializer.Serialize(this);
}

public sealed record AppearanceLoadResult(DisplayPreferences Preferences, string? Warning)
{
    public static AppearanceLoadResult Invalid { get; } = new(DisplayPreferences.Default,
        "Previous preferences could not be loaded. Defaults are shown; Apply replaces the saved file.");
}

public sealed record AppearanceSaveResult(bool Succeeded, string? Error)
{
    public static AppearanceSaveResult Saved { get; } = new(true, null);
    public static AppearanceSaveResult Failed { get; } = new(false, "Preferences could not be saved. Retry or Cancel; the earlier file is kept.");
}

public interface IAppearanceStore
{
    AppearanceLoadResult Load();
    Task<AppearanceSaveResult> SaveAsync(DisplayPreferences preferences);
}
