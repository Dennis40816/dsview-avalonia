<!-- SPDX-FileCopyrightText: 2026 Dennis40816 -->
<!-- SPDX-License-Identifier: GPL-3.0-or-later -->
# Inspecting decoded evidence

Select a decoded byte on the waveform or in the packet inspector's Hex sequence.
The inspector identifies its one-based data-byte number, value and decoded
interval. The waveform highlights that interval and fields whose source spans
overlap it. Address, ACK/NACK and START/STOP remain separate elements. Point
events have a position; the application does not invent a bit duration for them.

I²C START/repeated START markers are green, STOP is blue, ACK cells are muted
green and NACK cells are amber. NACK is a bus response and can end a normal
read; it does not by itself create a diagnostic. Rose marks diagnostic evidence.
Unknown acknowledgement remains neutral and is labeled `?` when space permits.
Labels accompany color: `S`/`Sr`/`P` for point events and `ACK`/`NACK` (or `A`/`N`
in narrower cells). A thin colored stripe remains when acknowledgement text
cannot fit. Selection adds a pale outline without changing the event's color;
the inspector shows its full identity in the same color. Zoomed-out packet
aggregates retain their compact data summary; individual events appear after
zooming. Waveform and physical-probe colors are independent of these markers.

Selection preserves time, Group and A/B. **Locate selected** explicitly frames
the element's source; **Previous** returns to the earlier view. **Whole packet**
returns to transaction selection. At coarse zoom, an aggregate selects the
whole packet. Copy byte copies only the selected data byte in Hex; Copy data
copies the packet's data bytes, excluding its address and protocol flags.

Byte selection uses packet identity plus the ordered element index. Repeated
Hex values remain distinct. Synthetic I2C wire cells provide explicit byte
spans. For native I2C annotations, byte association is available only when the
complete ordered data sequence matches the packet's address and bytes. A
partial or ambiguous correspondence provides no individual Hex-source action.
Other protocols without byte source metadata remain packet-level. No packet
duration is divided to estimate byte boundaries.

Decoded intervals are annotations, not raw-edge timing measurements. Their
coordinates may reach past EOF or overlap missing samples; selecting them
does not establish valid raw coverage. Use the signal measurement tools for
edge timing. Existing generated examples remain synthetic sources.

The inspector creates at most 64 byte controls at once and offers additional
pages when needed. Keyboard focus returns to a byte after selecting it in the
inspector. Selected protocol elements were introduced in analysis format
version 2. The current writer uses version 3, which also retains named timing
results. Version 1 documents reopen at packet level; versions 1 and 2 remain
readable and are upgraded to version 3 on Save. Older development builds
reject version 3, so keep an original file if it is needed by an older build.
See [Analysis files](ANALYSIS-FILES.md) for compatibility details.
