// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using DSView.Domain;

namespace DSView.Infrastructure;

public sealed record AcquisitionPreviewOptions(int WindowSamples = 65_536, int EdgesPerChannel = 2048)
{
    internal void Validate()
    {
        if(WindowSamples is < 1 or > 1_000_000 || EdgesPerChannel is < 1 or > 8192)
            throw new ArgumentOutOfRangeException(nameof(AcquisitionPreviewOptions));
    }
}

// Provisional, display-only evidence for [StartSample, EndSample). Coordinates are
// absolute acquisition samples. Initial is the level immediately before StartSample;
// an edge exactly at StartSample remains in Edges. No evidence outside this window.
// This is intentionally not an analysis Capture or a decoder input.
public sealed record AcquisitionPreview(Guid Operation, long SampleRate, long StartSample, long EndSample,
    bool WindowReduced, IReadOnlyList<Signal> Signals);
