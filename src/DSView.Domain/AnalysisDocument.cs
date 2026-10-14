// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
namespace DSView.Domain;

// Saved evidence is self-contained. No executable paths or decoder commands are accepted.
public sealed record AnalysisDocument(string Format, int Version, string SourceName, Capture Capture, AnalysisState State)
{
    public const string FileFormat = "DSView2 analysis";
    public const int CurrentVersion = 2;
}

public sealed record ViewAnchor(int? Channel, double Offset);
public sealed record AnalysisState(ObservationGroup[] Groups, string ActiveGroup, Dictionary<int, string> Colors,
    double Start, double End, Endpoint? A, Endpoint? B, int Mode, bool LocalPulse,
    string? SelectedPacket, string? SelectedIssue, bool Details, bool PacketsTab, string Query,
    ViewAnchor Vertical, int? EdgeChannel)
{
    // Optional presentation state: v1 files have no selected protocol element.
    public int? SelectedProtocolIndex { get; init; }
}
