using TMap.Data;
using TMap.Protocol;

namespace TMap.Server.Map;

/// <summary>
/// Name and country changes — C++ <c>OnCS_CHANGENAME_REQ</c> (CSHandler.cpp:14634) with its duplicate check
/// (<c>TCheckDuplicateName</c>, <c>OnDM_CHECKCHANGENAME_ACK</c>), and <c>OnCS_CHANGECOUNTRY_REQ</c> (CSHandler.cpp:17131).
/// <list type="bullet">
/// <item><b>A new name</b> takes a name-change item. The name is letters, digits, the German umlauts and spaces, and no
/// character, NPC or monster may have it already. The change goes round the world (friends, guild, the name index) and
/// comes back to every map. The item is used up.</item>
/// <item><b>A new country</b> is not possible in a party, as a mercenary or in a guild. From the peace country it takes
/// level 9 and goes to Defugel or Craxion. Otherwise it takes level 130, and only up to level 179. Defugel or Craxion can
/// only go to Broa. Broa can only go back to the original country, which takes a country item and also drops the aid
/// country. The new country's start point becomes the spawn point. Summons (not mounts) and placed objects go.</item>
/// <item><b>An aid country</b> (Broa players only, from level 130) is Defugel, Craxion or none, and can be changed once a
/// day.</item>
/// </list>
/// <para>The world applies the change (party, guild and friends for a new country) and sends it back
/// (<c>MW_CHANGECHARBASE_REQ</c>, MapService.Title.cs). It is saved with <c>TSaveCharBase</c>. <b>Not ported:</b> the
/// occupation heroes and tournament names a new name would update (no territory wars yet).</para>
/// </summary>
public sealed partial class MapService
{
    private const byte IkName = 48, IkCountry = 96, IkAidCountry = 97;
    private const byte CcbDuplicate = 1, CcbNoItem = 2, CcbTime = 3, CcbParty = 4, CcbTactics = 5, CcbGuild = 6, CcbLevel = 8, CcbFail = 9;
    private const byte BroaBaseLevel = 130, WarCountryMaxGap = 5, ChoiceCountryLevel = 9, TcontryD = 0, TcontryC = 1;

    // ================================ name ================================

    private async Task OnCS_CHANGENAME_REQ(ClientSession s, PacketReader r)
    {
        if (s.State != EnterState.InGame || !s.IsMain || s.Char is not { } ch || IsTutorial(ch)) return;
        byte inv = r.ReadByte(), slot = r.ReadByte();
        string name = r.ReadString();

        if (ch.FindInven(inv)?.FindItem(slot) is not { Count: > 0, Template: { Type: ItUse, Kind: IkName } }) return;
        if (name.Length == 0 || Cp949Length(name) > MaxName || !CheckCharName(name))
        {
            SendCS_CHANGECHARBASE_ACK(s, CcbFail, ch.CharId, IkName, 0, name, ch.TitleId);
            return;
        }

        byte ret;
        if (_gameDb is null) ret = FindByName(name) is null ? (byte)0 : CcbDuplicate;    // DB-free: the names online
        else
        {
            try { ret = (byte)await _gameDb.CheckDuplicateNameAsync(ch.CharId, name); }
            catch (Exception ex) { _log.LogWarning(ex, "TCheckDuplicateName failed for char {Char}.", ch.CharId); ret = CcbFail; }
        }

        // C++ OnDM_CHECKCHANGENAME_ACK: the player may have moved on meanwhile.
        if (s.State != EnterState.InGame || s.Char != ch || s.Store.IsOpen || s.Deal.InProgress) return;
        if (ret != 0) { SendCS_CHANGECHARBASE_ACK(s, ret, ch.CharId, IkName, 0, name, ch.TitleId); return; }
        var bag = ch.FindInven(inv);
        if (bag?.FindItem(slot) is not { } item) { SendCS_CHANGECHARBASE_ACK(s, CcbNoItem, ch.CharId, IkName, 0, name, ch.TitleId); return; }

        ChangeCharBase(s, ch, IkName, 0, name);
        UseItemStack(s, inv, bag, item, 1);
        SendCS_MOVEITEM_ACK(s, MoveItemResult.Success);
    }

    /// <summary>C++ <c>CTMapSvrModule::CheckCharName</c> (TMapSvr.cpp:10899), the same for every nation: digits, a–z, A–Z,
    /// ÄÖÜßäöü and spaces.</summary>
    public static bool CheckCharName(string name)
        => name.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'z' or >= 'A' and <= 'Z' or ' '
            or 'Ä' or 'Ö' or 'Ü' or 'ß' or 'ä' or 'ö' or 'ü');

    // ================================ country ================================

    private void OnCS_CHANGECOUNTRY_REQ(ClientSession s, PacketReader r)
    {
        if (s.State != EnterState.InGame || !s.IsMain || s.Char is not { } ch) return;
        byte type = r.ReadByte(), country = r.ReadByte();
        byte inv = Proto.InvenNull, slot = Proto.InvalidSlot;
        if (r.Remaining >= 2) { inv = r.ReadByte(); slot = r.ReadByte(); }
        void Fail(byte result, uint second = 0) => SendCS_CHANGECHARBASE_ACK(s, result, ch.CharId, type, country, "", ch.TitleId, second);

        if (ch.PartyId != 0) { Fail(CcbParty); return; }
        if (ch.TacticsId != 0) { Fail(CcbTactics); return; }

        if (type == IkAidCountry)
        {
            if (ch.AidCountry == country) return;
            if (ch.Level < BroaBaseLevel) { Fail(CcbLevel); return; }
            if (AidLeftTime(ch) is > 0 and var left) { Fail(CcbTime, left); return; }
            if (ch.Country != TcontryB || country == TcontryB) { Fail(CcbFail); return; }
        }
        else
        {
            if (ch.Country == country) return;
            if (ch.GuildId != 0) { Fail(CcbGuild); return; }
            if (ch.Country != TcontryPeace)
            {
                if (ch.Level < BroaBaseLevel) { Fail(CcbLevel); return; }
                if ((ch.Level - BroaBaseLevel) / 10 >= WarCountryMaxGap) return;
                if (ch.Country == TcontryB)
                {
                    var bag = ch.FindInven(inv);
                    if (bag?.FindItem(slot) is not { Template.Kind: IkCountry } item) { Fail(CcbNoItem); return; }
                    country = ch.OriCountry;
                    if (ch.AidCountry != TcontryN) ChangeCharBase(s, ch, IkAidCountry, TcontryN, "");
                    UseItemStack(s, inv, bag, item, 1);
                    SendCS_MOVEITEM_ACK(s, MoveItemResult.Success);
                }
                else if (country != TcontryB) return;
            }
            else
            {
                if (ch.Level < ChoiceCountryLevel) { Fail(CcbLevel); return; }
                if (country >= TcontryB) return;
            }

            ushort portal = country switch { TcontryD => 15001, TcontryC => 15002, TcontryB => 15006, _ => 0 };   // TSTART_*_PORTAL_ID
            if (_templates.Portals.TryGetValue(portal, out var pt)) ch.Persist.SpawnId = pt.SpawnId;
        }

        foreach (var m in ch.Recalls.Values.Where(m => m.RecallType != RecallMon.TypePet).ToList())
            SendMW_RECALLMONDEL_ACK(ch.CharId, s.Key, m.Id);
        ClearSelfObjs(ch);
        ChangeCharBase(s, ch, type, country, "");
    }

    /// <summary>C++ <c>CTPlayer::GetAidLeftTime</c> (TPlayer.cpp:6410): the seconds until the aid country may change again —
    /// a day after the last change, none without an aid country.</summary>
    private uint AidLeftTime(Character ch)
    {
        if (ch.AidCountry >= TcontryB) return 0;
        long now = UnixNow();
        return now >= DayOne + ch.AidDate ? 0 : (uint)(ch.AidDate + DayOne - now);
    }

    // ================================ the change ================================

    /// <summary>C++ <c>CTPlayer::ChangeCharBase</c> (TPlayer.cpp:5054) once the value is known: a new country moves the player
    /// between the countries' month ladders; then the world is told (<c>MW_CHANGECHARBASE_ACK</c>) and it is saved.</summary>
    private void ChangeCharBase(ClientSession s, Character ch, byte kind, byte value, string name)
    {
        if (kind == IkCountry)
        {
            CheckMonthRank(ch, ch.Country, 0, 0);
            CheckMonthRank(ch, value, ch.MonthPvPoint, ch.PvpTotalPoint);
        }
        var w = new PacketWriter(Msg.MW_CHANGECHARBASE_ACK);
        w.WriteUInt32(ch.CharId); w.WriteUInt32(s.Key); w.WriteByte(kind); w.WriteByte(value); w.WriteUInt16(ch.TitleId); w.WriteString(name);
        _world.Send(w);
        if (_gameDb is { } db) { uint id = ch.CharId; _ = EnqueueDbWrite(() => db.SaveCharBaseAsync(id, kind, value, name)); }
    }

    /// <summary>The name / country / aid-country part of C++ <c>OnMW_CHANGECHARBASE_REQ</c> (SSHandler.cpp:15209); returns the
    /// aid country's cooldown for the answer.</summary>
    private uint ApplyCharBase(Character ch, byte kind, byte value, string name)
    {
        switch (kind)
        {
            case IkName: ch.Name = name; break;
            case IkCountry: ch.Country = value; break;
            case IkAidCountry:
                ch.AidCountry = value;
                ch.AidDate = value == TcontryN ? 0 : UnixNow();
                return AidLeftTime(ch);
        }
        return 0;
    }

    private static bool IsCharBaseKind(byte kind) => kind is IkName or IkCountry or IkAidCountry;

    /// <summary>C++ <c>SendCS_CHANGECHARBASE_ACK</c> (CSSender.cpp:4968), to the player alone.</summary>
    private static void SendCS_CHANGECHARBASE_ACK(ClientSession s, byte result, uint charId, byte kind, byte value, string name,
        ushort titleId, uint second = 0) => s.Send(BuildCS_CHANGECHARBASE_ACK(result, charId, kind, value, name, titleId, second));

    private static byte[] BuildCS_CHANGECHARBASE_ACK(byte result, uint charId, byte kind, byte value, string name, ushort titleId, uint second)
    {
        var w = new PacketWriter(Msg.CS_CHANGECHARBASE_ACK);
        w.WriteByte(result); w.WriteUInt32(charId); w.WriteByte(kind); w.WriteByte(value); w.WriteString(name);
        w.WriteUInt16(titleId); w.WriteUInt32(second);
        return w.ToArray();
    }
}
