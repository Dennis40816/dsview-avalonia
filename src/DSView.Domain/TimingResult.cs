// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
namespace DSView.Domain;

// Capture-owned source identities, never formatted values or live references to the working pair.
public sealed record TimingResult(string Id, string Name, string Note, Endpoint A, Endpoint B, bool LocalPulse)
{
    public const int MaximumCount = 256;
    public const int MaximumName = 80;
    public const int MaximumNote = 2048;
    public override string ToString() => Name;
    public static bool ValidMetadata(string? name, string? note) => !string.IsNullOrWhiteSpace(name)
        && name.Length <= MaximumName && !name.Any(char.IsControl) && note != null && note.Length <= MaximumNote
        && !note.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t'));
}
