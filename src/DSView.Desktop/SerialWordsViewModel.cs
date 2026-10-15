// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DSView.Domain;

namespace DSView.Desktop;

internal sealed record SerialWordRow(string Label, string Value, bool Selected, IRelayCommand SelectCommand);

// A bounded projection of a packet; the Workbench remains the selection owner.
internal sealed class SerialWordsViewModel : ObservableObject
{
    public const int PageSize = 32;
    private readonly Workbench model;
    private readonly Capture capture;
    private readonly Packet packet;
    private readonly (ProtocolItem Item, int Index)[] words;
    private int page;
    public SerialWordsViewModel(Workbench model, Packet packet)
    {
        this.model = model; capture = model.Capture!; this.packet = packet;
        words = capture.ProtocolItems(packet).Select((item, index) => (Item: item, Index: index)).Where(x => x.Item.Serial != null).ToArray();
        int selected = Array.FindIndex(words, x => x.Index == model.SelectedProtocolIndex);
        page = Math.Max(0, selected) / PageSize;
        PreviousCommand = new RelayCommand(() => Move(-1), () => page > 0);
        NextCommand = new RelayCommand(() => Move(1), () => (page + 1) * PageSize < words.Length);
    }
    public IReadOnlyList<SerialWordRow> Rows => words.Skip(page * PageSize).Take(PageSize).Select(x =>
        new SerialWordRow($"{x.Item.Serial!.Direction} · {x.Item.Serial.Index + 1} · {x.Item.Serial.Word.BitWidth} bit",
            x.Item.Serial.Word.Display, x.Index == model.SelectedProtocolIndex,
            new RelayCommand(() => { if (IsCurrent) model.SelectProtocol(packet, x.Index); }))).ToArray();
    public bool HasWords => words.Length > 0;
    public bool HasPages => words.Length > PageSize;
    public string Range => $"Words {page * PageSize + 1}–{Math.Min(words.Length, (page + 1) * PageSize)} of {words.Length}";
    public string CopyText => string.Join(Environment.NewLine, words.Select(x => x.Item.Serial!.Label));
    public bool IsCurrent => ReferenceEquals(capture, model.Capture) && capture.Packets.Any(p => ReferenceEquals(p, packet));
    public IRelayCommand PreviousCommand { get; }
    public IRelayCommand NextCommand { get; }
    private void Move(int delta)
    {
        page = Math.Clamp(page + delta, 0, Math.Max(0, (words.Length - 1) / PageSize));
        OnPropertyChanged(nameof(Rows)); OnPropertyChanged(nameof(Range));
        PreviousCommand.NotifyCanExecuteChanged(); NextCommand.NotifyCanExecuteChanged();
    }
}
