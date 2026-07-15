using TMap.Protocol;

namespace TMap.Server.Tests;

/// <summary>
/// Test-side parsers that mirror the map's serializers byte-for-byte (CTItem::WrapPacketClient,
/// SendCS_CHARINFO_ACK, SendCS_ENTER_ACK). Validating a captured packet through these proves the
/// on-wire layout end-to-end.
/// </summary>
internal static class Wire
{
    public sealed record ParsedItem(
        byte Slot, ushort TemplateId, byte Level, byte Gem, ushort MoggItemId, ushort Companion, byte Count,
        uint DuraMax, uint DuraCur, byte RefineMax, byte RefineCur, byte GLevel, long EndTime, byte GradeEffect,
        byte Eld, byte Wrap, ushort Color, ushort CustomTex, byte RegGuild, List<(byte Id, ushort Value)> Magic);

    public sealed record ParsedInven(byte InvenId, ushort TemplateId, long EndTime, List<ParsedItem> Items);
    public sealed record ParsedSkill(ushort Id, byte Level, uint Reuse);
    public sealed record ParsedHotkey(byte InvenKey, (byte Type, ushort Id)[] Slots);
    public sealed record ParsedCharInfo(uint CharId, string Name, List<ParsedInven> Invens,
        List<ParsedSkill> Skills, List<ParsedHotkey> Hotkeys, uint MaxHp, uint Hp, uint MaxMp, uint Mp);

    public static ParsedItem ParseItem(PacketReader r)
    {
        byte slot = r.ReadByte();
        ushort template = r.ReadUInt16();
        byte level = r.ReadByte();
        byte gem = r.ReadByte();
        ushort mogg = r.ReadUInt16();
        ushort companion = r.ReadUInt16();
        byte count = r.ReadByte();
        uint duraMax = r.ReadUInt32();
        uint duraCur = r.ReadUInt32();
        byte refineMax = r.ReadByte();
        byte refineCur = r.ReadByte();
        byte glevel = r.ReadByte();
        long endTime = r.ReadInt64();
        byte grade = r.ReadByte();
        byte eld = r.ReadByte();
        byte wrap = r.ReadByte();
        ushort color = r.ReadUInt16();
        ushort customTex = r.ReadUInt16();
        byte regGuild = r.ReadByte();
        int magicCount = r.ReadByte();
        var magic = new List<(byte, ushort)>();
        for (int i = 0; i < magicCount; i++) magic.Add((r.ReadByte(), r.ReadUInt16()));
        return new ParsedItem(slot, template, level, gem, mogg, companion, count, duraMax, duraCur,
            refineMax, refineCur, glevel, endTime, grade, eld, wrap, color, customTex, regGuild, magic);
    }

    private static void SkipMaintain(PacketReader r)
    {
        int count = r.ReadByte();
        for (int i = 0; i < count; i++)
        {
            r.ReadUInt16(); r.ReadByte(); r.ReadUInt32(); r.ReadUInt32(); r.ReadByte(); r.ReadUInt32();
            r.ReadByte(); r.ReadByte(); r.ReadUInt16(); r.ReadByte(); r.ReadUInt32(); r.ReadUInt32();
            r.ReadUInt32(); r.ReadUInt32(); r.ReadByte(); r.ReadByte(); r.ReadFloat(); r.ReadFloat(); r.ReadFloat();
        }
    }

    public static ParsedCharInfo ParseCharInfo(byte[] p)
    {
        var r = new PacketReader(p);
        uint charId = r.ReadUInt32();
        r.ReadByte(); r.ReadByte(); r.ReadByte();   // secure trio
        r.ReadUInt16();                              // titleId
        string name = r.ReadString();
        r.ReadByte();                                // startAct
        for (int i = 0; i < 13; i++) r.ReadByte();   // appearance + level
        r.ReadUInt16();                              // partyId
        r.ReadUInt32(); r.ReadUInt32(); r.ReadUInt32(); // guildId, fame, fameColor
        r.ReadByte(); r.ReadByte();                  // duty, peer
        r.ReadString();                              // guildName
        r.ReadUInt32();                              // tacticsId
        r.ReadString();                              // tacticsName
        r.ReadUInt32(); r.ReadUInt32(); r.ReadUInt32(); // gold, silver, cooper
        r.ReadUInt32(); r.ReadUInt32(); r.ReadUInt32(); // prevExp, nextExp, exp
        uint maxHp = r.ReadUInt32(), hp = r.ReadUInt32(), maxMp = r.ReadUInt32(), mp = r.ReadUInt32();
        r.ReadUInt32();                              // partyChiefId
        r.ReadUInt16();                              // commanderId
        r.ReadUInt32();                              // region
        r.ReadUInt16();                              // mapId
        r.ReadFloat(); r.ReadFloat(); r.ReadFloat(); // pos
        r.ReadUInt16();                              // dir
        r.ReadUInt16();                              // skillPoint
        r.ReadByte();                                // luckyNumber
        r.ReadUInt32();                              // aidLeftTime
        r.ReadUInt16(); r.ReadUInt16(); r.ReadUInt16(); r.ReadUInt16(); // skill-kind points
        r.ReadUInt32();                              // rankPoint
        r.ReadByte();                                // non-BOW flag

        var invens = new List<ParsedInven>();
        int invenCount = r.ReadByte();
        for (int i = 0; i < invenCount; i++)
        {
            byte invenId = r.ReadByte();
            ushort template = r.ReadUInt16();
            long endTime = r.ReadInt64();
            int itemCount = r.ReadByte();
            var items = new List<ParsedItem>();
            for (int j = 0; j < itemCount; j++) items.Add(ParseItem(r));
            invens.Add(new ParsedInven(invenId, template, endTime, items));
        }

        var skills = new List<ParsedSkill>();
        int skillCount = r.ReadByte();
        for (int i = 0; i < skillCount; i++)
            skills.Add(new ParsedSkill(r.ReadUInt16(), r.ReadByte(), r.ReadUInt32()));

        SkipMaintain(r);

        var hotkeys = new List<ParsedHotkey>();
        int pageCount = r.ReadByte();
        for (int i = 0; i < pageCount; i++)
        {
            byte invenKey = r.ReadByte();
            var slots = new (byte, ushort)[12];
            for (int s = 0; s < 12; s++) slots[s] = (r.ReadByte(), r.ReadUInt16());
            hotkeys.Add(new ParsedHotkey(invenKey, slots));
        }

        return new ParsedCharInfo(charId, name, invens, skills, hotkeys, maxHp, hp, maxMp, mp);
    }

    /// <summary>Walks CS_ENTER_ACK to its equipped-item sub-loop and returns those items.</summary>
    public static List<ParsedItem> ParseEnterEquip(byte[] p)
    {
        var r = new PacketReader(p);
        r.ReadUInt32();       // charId
        r.ReadString();       // name
        r.ReadUInt16();       // titleId
        r.ReadString();       // comment
        r.ReadUInt32(); r.ReadUInt32(); r.ReadUInt32(); // guildId, fame, fameColor
        r.ReadString();       // guildName
        r.ReadByte();         // guildPeer
        r.ReadUInt32();       // tacticsId
        r.ReadString();       // tacticsName
        r.ReadByte();         // store
        r.ReadString();       // storeName
        r.ReadUInt32();       // riding
        for (int i = 0; i < 13; i++) r.ReadByte(); // appearance + level + helmetHide
        r.ReadUInt32(); r.ReadUInt32(); r.ReadUInt32(); r.ReadUInt32(); // maxHP, HP, maxMP, MP
        r.ReadUInt32();       // partyChiefId
        r.ReadUInt16();       // partyId
        r.ReadUInt16();       // commanderId
        r.ReadFloat(); r.ReadFloat(); r.ReadFloat(); // pos
        r.ReadByte(); r.ReadByte(); r.ReadByte();   // action, block, mode
        r.ReadUInt16(); r.ReadUInt16();             // pitch, dir
        r.ReadByte(); r.ReadByte();                 // mouseDir, keyDir
        r.ReadByte();         // color
        r.ReadUInt32();       // region
        r.ReadByte();         // inPcBang
        r.ReadByte();         // aftermath step
        r.ReadUInt32();       // rankPoint
        r.ReadUInt16();       // castle
        r.ReadByte();         // camp
        r.ReadUInt16();       // godBall
        SkipMaintain(r);
        int equipCount = r.ReadByte();
        var items = new List<ParsedItem>();
        for (int i = 0; i < equipCount; i++) items.Add(ParseItem(r));
        return items;
    }
}
