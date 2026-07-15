namespace TMap.Server.Map;

/// <summary>One hotkey slot: a typed reference (skill/item/etc) the client places on its action bar.</summary>
public readonly record struct HotkeySlot(byte Type, ushort Id);

/// <summary>
/// A hotkey page keyed by an inventory id — the CS_CHARINFO_ACK hotkey sub-loop emits, per page,
/// <c>MAX_HOTKEY_POS</c> (12) slots in order. Port of the <c>m_mapHotkeyInven</c> entries.
/// </summary>
public sealed class HotkeyPage
{
    public const int SlotCount = 12; // MAX_HOTKEY_POS

    public byte InvenKey { get; set; }
    public HotkeySlot[] Slots { get; } = new HotkeySlot[SlotCount];
}
