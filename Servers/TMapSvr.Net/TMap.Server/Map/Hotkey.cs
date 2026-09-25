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

    /// <summary>C++ <c>m_bSave</c> (<c>ITEM_SAVE_*</c> bits): <see cref="SaveLoad"/> when read from the DB or just
    /// saved, <see cref="SaveInsert"/> for a page first created this session, <see cref="SaveUpdate"/> or'd in on
    /// every edit. <c>SendDM_SAVECHAR_REQ</c> turns it into the <c>TSaveHotkey</c> verb.</summary>
    public byte Save { get; set; } = SaveLoad;

    public const byte SaveUpdate = 2, SaveInsert = 4, SaveLoad = 16;   // ITEM_SAVE (TMapType.h:272)

    public bool IsEmpty => Slots.All(k => k.Type == 0);                  // CTPlayer::IsEmptyHotkey — HOTKEY_NONE
    public HotkeySlot[] Slots { get; } = new HotkeySlot[SlotCount];
}
