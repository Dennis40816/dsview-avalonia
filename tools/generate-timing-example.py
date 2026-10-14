# SPDX-FileCopyrightText: 2026 Dennis40816
# SPDX-License-Identifier: GPL-3.0-or-later
"""Generate synthetic transition evidence and explicit annotations, not a decoder."""
import json
from pathlib import Path

channels = [{"id": i, "name": name, "initial": 1, "events": []}
            for i, name in enumerate(["SCL", "SDA", "SPI_CLK", "MOSI", "MISO", "CS_N",
                                      "UART_TX", "UART_RX", "RESET_N", "PWR_GOOD"]
                                     + [f"AUX_{i}" for i in range(10, 32)])]
records = []


def transition(channel, time_us, level):
    events = channels[channel]["events"]
    previous = events[-1]["v"] if events else channels[channel]["initial"]
    if previous != level:
        events.append({"t": time_us, "v": level})


def transfer(identifier, start, read, data, acknowledged, title):
    address = 0x51
    wire_bytes = [(address << 1) | int(read)] + data
    transition(1, start, 0)  # START while SCL is high.
    cell_start = start + 10
    cells = []
    for byte, ack in zip(wire_bytes, acknowledged, strict=True):
        cells.append({"data": [cell_start, cell_start + 80],
                      "ack": [cell_start + 80, cell_start + 90]})
        for bit, value in enumerate([(byte >> shift) & 1 for shift in range(7, -1, -1)]
                                    + [int(not ack)]):
            time = cell_start + bit * 10
            transition(0, time, 0)
            transition(1, time + 1, value)
            transition(0, time + 5, 1)
        cell_start += 90
    # STOP setup after NACK: lower SCL before SDA, then raise SCL before SDA.
    transition(0, cell_start, 0)
    transition(1, cell_start + 1, 0)
    transition(0, cell_start + 5, 1)
    transition(1, cell_start + 10, 1)
    record = {"id": identifier, "group": "i2c", "start": start,
              "end": cell_start + 10, "bytes": data, "address": "0x51",
              "wire": {"address7bit": address, "addressByte": wire_bytes[0],
                       "direction": "Read" if read else "Write",
                       "acknowledged": acknowledged, "cells": cells},
              "title": title, "error": None}
    records.append(record)
    return record


read = transfer("alt-read-1", 1000, True, [0xA5, 0x21, 0x02, 0x78, 0x56, 0],
                [True] * 6 + [False], "Sensor reply")
read["value"] = 0x5678
read["fields"] = [{"name": name, "raw": raw, "spans": spans} for name, raw, spans in [
    ("Sync", [0xA5], [[1100, 1180]]), ("Header", [0x21], [[1190, 1270]]),
    ("Length", [2], [[1280, 1360]]), ("Payload", [0x78, 0x56], [[1370, 1450], [1460, 1540]]),
    ("Checksum", [0], [[1550, 1630]])]]
read["error"] = "Checksum mismatch"
read["diagnostics"] = [{"id": "alt-read-1/checksum", "layer": "DemoFrame", "code": "checksum",
                        "message": "Checksum mismatch: received 00; expected A8 (XOR of prior data bytes)",
                        "spans": [[1550, 1630]], "field": "Checksum"}]
nack = transfer("alt-address-nack", 5000, False, [], [False], "Address not acknowledged")
nack["error"] = "Address NACK"
nack["diagnostics"] = [{"id": "alt-address-nack/address", "layer": "I²C", "code": "address-nack",
                        "message": "Address not acknowledged", "spans": [[5090, 5100]]}]
channels[8]["initial"] = 0
for time, level in [(900, 1), (1700, 0), (1800, 1), (4050, 0), (4700, 1)]:
    transition(8, time, level)
for channel in (2, 10, 11):
    for time, level in [(2000 + channel * 10, 0), (2100 + channel * 10, 1), (6000, 0), (6200, 1)]:
        transition(channel, time, level)

fixture = {"sampleRate": 10_000_000, "sampleCount": 80_000, "gaps": [[4000, 4500]],
           "channels": channels, "records": records}
destination = Path(__file__).resolve().parents[1] / "src/DSView.Infrastructure/Fixtures/timing-gaps.json"
destination.write_text(json.dumps(fixture, separators=(",", ":")) + "\n", encoding="utf-8")
print(destination)
