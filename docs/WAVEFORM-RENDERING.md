<!--
SPDX-FileCopyrightText: 2026 Dennis40816
SPDX-License-Identifier: GPL-3.0-or-later
-->

# Waveform rendering

The display limits waveform preparation by viewport width while retaining the
original transition data for picking and measurement. This separates display
detail from timing evidence. It does not reduce the stored capture or establish
an end-to-end acquisition performance guarantee.

## Projection and caching

`WaveformProjection.Create` locates the visible transition interval by binary
search. Sparse intervals retain each transition. Dense intervals partition that
interval into display columns and retain each occupied column's first edge,
last edge and transition count. Exact integer boundary conversion prevents an
edge from moving to an adjacent bin through floating-point rounding.

A multi-edge column is rendered as an activity band between its source extents.
It is not a representative pulse train. A short pulse remains visible when its
entering and exiting levels are equal. Zooming changes the projection; it never
changes the sample indices used by edge selection, pulse timing or A/B timing.

`TimelineControl` caches row-local trace geometry by capture identity, time
range, plot width and signal identity. Group order and vertical scrolling do not
become part of cached Y coordinates. Colors are applied during drawing. Capture,
time or width changes invalidate the cache; signals outside the vertical viewport
are skipped. The time ruler remains outside the scrolling signal rows.

Missing-sample hatching and unknown-level overlays remain separate from dense
rendering. Only an observed transition after a gap restores level evidence.
Activity counts subtract the union of overlapping missing intervals, and edge
navigation jumps over whole missing intervals instead of walking their edges.
The Group overview queries observed transition counts in exact sample bins.

## Time labels

`TimeAxis` chooses a shared unit from the viewport span and produces bounded
1/2/5 ticks. Fine views at large absolute times declare an origin and use
relative labels. The native renderer measures label widths and omits collisions;
tick positions still use the common sample-to-X projection.

Integer and fractional parts are converted separately to avoid losing integer
sample digits through direct double/decimal conversion. Endpoint input is parsed
and rounded using decimal arithmetic in explicit microseconds. Neither path
parses display labels to obtain waveform coordinates. The viewport itself still
uses doubles: these changes do not establish individual-sample precision above
2^53 samples or complete long-recording support.

## Correctness and work scaling

Run the default focused checks after a build:

```sh
dotnet run --project tests/DSView.Checks --no-build --no-restore
```

These include exact boundary edges, fractional viewports, a one-sample pulse,
random irregular traces, overlapping unsorted gaps and source-based timing on
the generated Dense signals example.

Add the optional operation-count experiment:

```sh
dotnet run --project tests/DSView.Checks --no-build --no-restore -- --performance
```

`CountedEdges` counts indexed reads and enumeration. The experiment compares
100,000 and 1,000,000 stored transitions with a fixed 1024-column projection,
then exercises gapped queries, a 32-channel overview and 32 trace projections.
It checks source coverage, bounded run counts and read-count budgets. The
32-channel algorithm case shares one immutable transition array; the selectable
Dense signals example instead has 32 independent arrays with 32,768 edges each.

Elapsed time and allocation output are diagnostics from one warmed invocation,
not benchmark distributions or pass thresholds. These checks do not measure
native frame time, input latency, process memory, GPU work, acquisition contention
or decoder throughput. Projection scales with display columns and binary
searches, but gap processing still depends on the number of gaps. Packet drawing
and other UI refresh paths require separate profiling before claiming that the
whole workbench scales to arbitrary recordings.
