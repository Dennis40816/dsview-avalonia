<!--
SPDX-FileCopyrightText: 2026 Dennis40816
SPDX-License-Identifier: GPL-3.0-or-later
-->

# DSView capability parity and first hardware demo

Owner decision, 2026-10-15: preserve the scope of existing DSView functionality,
while simplifying how it is presented. The first hardware target is tentatively
DSLogic Plus; the owner reports that it is **not connected**. The actual model,
revision, USB identity, firmware and capabilities have not been verified.

This is a first source-backed inventory, not an exhaustive runtime certification.
Upstream was inspected read-only at
[`2e9e2c8e726df4ef5687d39b83d4f797cc44b574`](https://github.com/DreamSourceLab/DSView/tree/2e9e2c8e726df4ef5687d39b83d4f797cc44b574).
File references below are relative to that tree; `pv/` abbreviates `DSView/pv/`
and `hardware/` abbreviates `libsigrok4DSL/hardware/`. The current application is still
an offline workbench. No device was enumerated, configured or acquired during
this review. A capability marked partial must not be advertised as full parity.

## Device identity matters

`libsigrok4DSL/hardware/DSL/dsl.h:508`, `:724`, and `:778` contain three
DSLogic PLus profiles with product IDs `0x0020`, `0x0030`, and `0x0034` and
different firmware/FPGA resource names. The profiles declare logic mode, variable
threshold and buffered acquisition, with different additional flags. They list
16 channels and different stream/buffer channel-rate combinations. These are
source declarations, not measurements or confirmation of the owner's device.

Enumeration must report the actual identity, then read supported configuration
lists from the active device. Do not select a firmware or a rate/channel preset
from the marketing name alone. Do not present oscilloscope controls as a DSLogic
Plus capability; retain them in the product roadmap for applicable devices.

## Capability inventory

**Partial** means some corresponding workbench behavior exists; missing items
are stated explicitly. **Open** means no integrated feature. The proposed UI
location is a design decision, not evidence of implementation.

| Capability | Upstream evidence | DSView2 status / gap | Intended location |
| --- | --- | --- | --- |
| Device enumeration, selection, hotplug | `libsigrok4DSL/lib_main.c:115,284`; `DSView/pv/toolbars/samplingbar.cpp:1142`; `pv/sigsession.cpp:2125` | Partial descriptor-only USB backend; App device selection, connection and hotplug lifecycle remain open | Source/device selector |
| Device capability and operating-mode selection | `pv/dialogs/deviceoptions.cpp:160,306,458` | Open; runtime capability lists required | Capture setup |
| Sampling rate and capture duration/depth | `pv/toolbars/samplingbar.cpp:418,558` | Open; offline sample rate is metadata only | Capture setup, short summary near Start |
| Single, repetitive and loop acquisition | `pv/toolbars/samplingbar.cpp:208`; `pv/sigsession.cpp:596,2112` | Open; modes need distinct lifecycle and cancellation tests | Capture action menu |
| Immediate acquisition / trigger bypass | `pv/toolbars/samplingbar.cpp:186`; `pv/sigsession.cpp:461` | Open | Capture action menu |
| Stop, transfer completion and partial/aborted data policy | `pv/sigsession.cpp:773,1458,2080`; `pv/dialogs/applicationpardlg.cpp:147` | Open; command acknowledgement must differ from end-of-data | Capture status |
| Overflow, disconnect, USB speed mismatch | `pv/sigsession.cpp:1449,2097,2132,2144` | Partial offline gaps/diagnostics; live error lifecycle open | Capture status + evidence |
| Per-channel acquisition enable; enable/disable all | `pv/dialogs/deviceoptions.cpp:229,393,604` | Open; Group visibility does not enable/disable capture | Channel settings |
| Channel name, probe mapping and hardware properties | `libsigrok4DSL/libsigrok.h:1518`; `pv/dialogs/deviceoptions.cpp:564,703` | Partial saved signal names/colors; hardware write/readback open | Channel settings |
| Digital thresholds and hardware filtering/options | `hardware/DSL/dsl.h` profiles; configuration APIs in `libsigrok.h:1489` | Open; inspect each device option before exposing it | Capture setup, advanced section |
| Per-channel rising/falling/either edge and high/low trigger | `pv/view/logicsignal.cpp:77` | Open; finding an edge in saved data is not a trigger | Channel settings + trigger summary |
| Trigger position/pretrigger proportion | `pv/dock/triggerdock.cpp:147` | Open | Capture setup |
| Multistage trigger, counters, inversion, AND/OR | `pv/dock/triggerdock.cpp:145,165,169,584` | Open; hardware/mode restrictions must be enforced | Advanced trigger editor |
| Serial trigger, clock/data selection, bits and value | `pv/dock/triggerdock.cpp:149,154,162,757` | Open | Advanced trigger editor |
| Time zoom, pan, full view, previous view | `pv/view/view.cpp`, `pv/view/ruler.cpp` | Partial implemented offline; newest wheel behavior needs native review | Waveform surface / local navigation |
| Digital signal rows, colors, names and ordering | `pv/view/logicsignal.cpp:189`; `pv/view/trace.cpp` | Partial; physical probe palette separate from custom line color; single-channel editor remains incomplete | Label rail / channel settings |
| Bus/group trace versus an observation Group | `pv/view/groupsignal.cpp` | Observation Groups exist; do not count them as upstream bus-value trace parity | Group panel; separate bus trace feature |
| Hover pulse width, period, frequency and duty | `pv/dock/measuredock.cpp:169,181` | Partial local pulse/cycle/duty from exact samples; invalid/incomplete cycles remain explicit | Waveform + compact result |
| Movable cursors, distances, sample counts | `pv/dock/measuredock.cpp:170,173,177`; `pv/view/cursor.cpp` | Partial A/B edge/position and saved results; direct-manipulation usability and multi-cursor parity open | Waveform cursors / results |
| Rising/falling/total edge counts between cursors | `pv/dock/measuredock.cpp:172,176` | Open as a dedicated measurement; activity counts are not this feature | Measurement result options |
| Raw pattern search, previous/next match | `pv/dialogs/search.cpp:94`; `pv/dock/searchdock.cpp:186,249` | Open; packet text search and single-edge navigation are not equivalent | Search panel |
| Decoder catalogue, add/remove, multiple instances and stacks | `pv/dock/protocoldock.cpp:296,344,419` | Partial I²C/SPI/UART instance workflow; catalogue, removal and stacks remain open | Decoder section in selected Group |
| Decoder role mapping, options and bounded decode interval | `pv/dialogs/decoderoptionsdlg.cpp:94,195,527` | Partial full-capture I²C/SPI/UART roles/options; bounded intervals and other protocols remain open | Decoder settings |
| Annotation rows, packet table, selection and source location | `pv/view/decodetrace.cpp`; `pv/dock/protocoldock.cpp:243` | Partial offline packet/Hex/field view; general annotation types and stack outputs open | Waveforms / Packets + inspector |
| Protocol search and export | `pv/dock/protocoldock.cpp:837`; `pv/dialogs/protocolexp.cpp:67` | Partial search/copy; protocol CSV export open | Packet tools |
| Native capture open/save and capture ranges | `pv/toolbars/filebar.cpp:111`; `pv/storesession.cpp:137,912` | Partial bounded logic-v3 import and separate `.dsview2` save; DSView writer/general formats open | File |
| Raw export formats and cancellation | `pv/storesession.cpp:119,805`; `libsigrok4DSL/output/output.c:71` | Open; enabled upstream list is CSV/VCD/gnuplot/srzip, not every declared output module | File → Export |
| Screenshot capture | `pv/toolbars/filebar.cpp:114` | Open | File / share action |
| Hardware/session profiles, restore defaults | `pv/toolbars/filebar.cpp:107` | Partial workbench preferences/analysis; acquisition profiles open | Capture setup / File |
| Global display preferences, themes and follow latest | `pv/dialogs/applicationpardlg.cpp:145`; `pv/toolbars/trigbar.cpp:140` | New appearance candidate; light theme/follow latest/other preferences open | Settings |
| Analog/DSO sample ingestion and rendering | `pv/sigsession.cpp:1437`; `pv/view/analogsignal.cpp:61`; `pv/view/dsosignal.cpp:255` | Open; retained in product scope, requires applicable hardware/source | Instrument workspace |
| Scope volts/div, offset, factor and coupling | `pv/view/dsosignal.cpp:282`; `pv/view/analogsignal.cpp:228` | Open | Scope channel settings |
| Scope trigger source, slope, holdoff and sensitivity | `pv/dock/dsotriggerdock.cpp:184` | Open | Scope capture setup |
| Scope measurements and calibration | `pv/dialogs/dsomeasure.cpp:78`; `pv/dialogs/deviceoptions.cpp:782` | Open; exact measurement list and safe calibration workflow need deeper review | Scope measurements / device service |
| FFT, source/window/length/DC/range controls | `pv/dialogs/fftoptions.cpp:198` | Open | Scope analysis tools |
| Math add/subtract/multiply/divide | `pv/dialogs/mathoptions.cpp:59` | Open | Scope analysis tools |
| Lissajous X/Y source view | `pv/dialogs/lissajousoptions.cpp:156` | Open | Scope analysis tools |

This first inventory must be extended by exercising the upstream app: keyboard
commands, context menus, exact supported analog measurements, acquisition
profiles, channel ordering and all failure/recovery paths remain to be compared.
Track them as open rather than treating a source filename as an acceptance pass.

## Reuse and protocol intake

Run `python tools/inventory-decoders.py /path/to/upstream inventory.json` to obtain
source paths, hashes and declared decoder metadata without executing decoder code.
At the pinned checkout it finds **150 `pd.py` modules / 150 declared IDs**:
86 declare `logic` input and 64 declare other input chains. This is not 150
independent protocols and not 150 working DSView2 decoders. Expressions that
cannot be read as literals are retained as unresolved expressions, not executed.

There are `0-` and `1-` variants of I2C, SPI and UART. For example `0:uart`
declares no output chain while `1:uart` declares `uart`; changing these silently
would break dependent decoders. Preserve variant identity and role/option schemas.

| Layer | Reuse decision | Work required in C# |
| --- | --- | --- |
| USB, hardware configuration, capture and firmware transport | Keep `libsigrok4DSL`; use an owned native worker with a versioned managed boundary | Device/capability model, async commands, lifecycle, bounded data transport, error translation |
| Existing base protocols and stacked decoders | Prefer `libsigrokdecode4DSL` and reviewed upstream Python modules | Role mapping, option validation, graph/layer identity, sample-span result adapter, cancellation and indexing |
| File readers/exporters | Reuse compatible native modules; current reader only proves a bounded subset | Validation, source identity, progress/cancellation and transactional replacement |
| Group, timeline, cursor and display state | Implement in managed domain/UI code | Exact sample arithmetic, viewport LOD, settings, accessibility and interaction |
| New public structured parsers | C# can implement the common parser contract when it reduces deployment/runtime cost | Golden fixtures, equivalent spans/fields/errors, versioned contract and measured performance |
| NVT proprietary parsing / Core components | Await the separate component and licensing agreement | Keep extension boundary; do not copy proprietary parser/theme code into this repository |

The catalogue's license fields are heterogeneous (including `agplv3` and the
nonstandard string `Pirate`). Those are source metadata facts, not legal
conclusions. Existing component review and packaging allowlists still apply;
inventorying a module does not approve redistribution. Do not bulk-bundle the
catalogue or rewrite it into C# as a substitute for reviewing its source terms.

Each intake row must record role mapping, options, parent outputs, annotation vs
structured/binary output support, raw fixture, independent expected values,
truncated/gapped input, cancellation, throughput and packaging provenance.
Start with current-capture I2C, then SPI/UART, then dependent stacks. Source-only
decoders remain pending until they pass these gates.

The [SPI/UART intake contract](PROTOCOL-INTAKE.md) records the local raw-input
experiment, upstream UART stop/parity limitations and required domain/UI changes.
Those protocols now use the App's shared current-capture decoder workflow.
Native controls, usability, real acquisition and throughput still need validation.

## Delivery order and acceptance

1. Finish the owner interaction revisions: Group sidebar, concise shell,
   Settings, channel controls, cursor manipulation, semantic markers and icons.
2. Introduce a device service over the existing native library. A worker owns
   initialization, device handles, callbacks and teardown. Enumerate real versus
   demo devices explicitly; implement no-device/disconnected states.
3. Configure supported LA channels/rate/depth/threshold/trigger; read back applied
   settings. Group visibility remains independent of acquired channels.
4. Run real single acquisition, Stop and recovery. Support buffered transfer and
   streaming according to reported capabilities; do not simulate live preview
   when buffered hardware has not transferred samples.
5. Decode acquired/imported raw data through explicit, capture-scoped roles;
   preserve physical IDs, source provenance and unrelated results.
6. Verify Group/zoom/measurement/save/reopen on the actual signal, and measure
   acquisition + decode + render together (throughput, loss, p95 input latency,
   frame time, memory/GC, decoder lag and cancellation).
7. Continue parity rows, including advanced LA and applicable analog/DSO features.
   A usable first demo is a milestone, not a declaration of full DSView parity.

Hardware acceptance remains pending until the owner connects the device. Work
on the implementation, source review and synthetic tests can proceed meanwhile.
