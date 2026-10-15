// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
namespace DSView.Domain;

public sealed record SpiOptions(int Mode, string BitOrder, int WordSize, string ChipSelectPolarity)
{
    public bool IsValid => Mode is >= 0 and <= 3 && WordSize is >= 5 and <= 128
        && BitOrder is "msb-first" or "lsb-first" && ChipSelectPolarity is "active-low" or "active-high";
}
public sealed record UartOptions(int BaudRate, int DataBits, string Parity, int StopBits, string BitOrder, bool Inverted)
{
    // The selected upstream module does not validate a second stop slot.
    public bool IsValid => BaudRate > 0 && DataBits is >= 4 and <= 128 && StopBits == 1
        && Parity is "none" or "odd" or "even" or "zero" or "one" && BitOrder is "msb-first" or "lsb-first";
}

// Fixed-width unsigned value, canonical uppercase hex, independent of wire bit order.
// Hex avoids JSON number precision loss for 9–128-bit values in other consumers.
public sealed record SerialWord(int BitWidth, string Hex, SourceSpan Span)
{
    public bool IsValid => BitWidth is >= 1 and <= 128 && Hex != null && Hex.Length == (BitWidth + 3) / 4
        && Hex.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F')
        && (BitWidth % 4 == 0 || Convert.ToInt32(Hex[..1], 16) < (1 << (BitWidth % 4)));
    public string Display => "0x" + Hex;
}
public sealed record SpiWord(SerialWord? Mosi, SerialWord? Miso);
public enum SpiCompletion { Unknown, ChipSelectReleased, CaptureEnded, WithoutChipSelect }
public sealed record SpiTransfer(SpiWord[] Words, SpiCompletion Completion);
public enum UartCompletion { Incomplete, StopObserved }
public sealed record UartFrame(SerialWord? Data, UartCompletion Completion, bool? Valid);

// A projection for waveform selection and the word inspector; it owns no data.
public sealed record SerialWordItem(string Direction, int Index, SerialWord Word)
{
    public string Label => $"{Direction} · {Word.Display}";
    public string Identity => $"{Direction} word {Index + 1} · {Word.BitWidth} bits · {Word.Display}";
}
