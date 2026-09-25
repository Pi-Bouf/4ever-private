using TMap.Protocol;

namespace TMap.Server.Map;

/// <summary>
/// Player revival — the C# port of <c>OnCS_REVIVAL_REQ</c> (CSHandler.cpp:1067) → <c>CTPlayer::Revival</c>
/// (TPlayer.cpp:3610). A dead player (Phase 20) chooses a revival point and type; the server repositions them
/// there (grid re-bucket + <c>CS_ENTER/LEAVE</c> diff, reusing the movement path), restores a fraction of
/// HP/MP by type, brings them back to <c>MT_NORMAL</c>, and broadcasts <c>CS_REVIVAL_ACK</c> + <c>CS_HPMP_ACK</c>
/// to the 3×3 view — closing the death loop the monster attack (Phase 20) opened.
///
/// <para><b>Restore fractions (value-exact):</b> <c>REVIVAL_NPC</c> (town, C++ AFTERMATH_ATONCE) = 30%,
/// <c>REVIVAL_GHOST</c> (in-place) = 40%; HP clamped to ≥ 1 (C++ <c>if(!m_dwHP) m_dwHP = 1</c>).</para>
///
/// <para>The death penalty, the revival-protection buff and priest resurrection live in
/// <c>MapService.Aftermath.cs</c> (<see cref="Revive"/>). <b>Deferred:</b> <c>RespawnCompanion</c> (pets) and
/// BoW/BR respawn placement.
/// A dead-only guard (<c>Hp == 0</c>) is added — the C++ handler has none, but a live client never sends this
/// and it prevents a stray request from teleporting/resetting a live player.</para>
/// </summary>
public sealed partial class MapService
{
    private void OnCS_REVIVAL_REQ(ClientSession s, PacketReader r)
    {
        float posX = r.ReadFloat();
        float posY = r.ReadFloat();
        float posZ = r.ReadFloat();
        byte type = r.ReadByte();   // REVIVAL_TYPE

        if (s.State != EnterState.InGame || s.Char is not { } ch) return;
        if (ch.Hp != 0) return;     // only a dead player revives (stricter than C++; a live client never sends this)

        // Reposition to the revival point (C++ m_fPosY + m_pMAP->OnMove → grid re-bucket + CS_ENTER/LEAVE diff).
        ch.PosY = posY;
        RelocateAndExchangeView(s, posX, posY, posZ);

        // C++ Revival(bType == REVIVAL_NPC ? AFTERMATH_ATONCE : AFTERMATH_GHOST): the death penalty, then HP/MP
        // by kind (30% / 40%, HP ≥ 1), back to NORMAL, the broadcast and the revival-protection buff.
        if (s.IsMain) Revive(s, ch, type == (byte)RevivalType.Npc ? AftermathAtOnce : AftermathGhost, null, 0);
    }

    /// <summary>C++ <c>SendCS_REVIVAL_ACK</c> (CSSender.cpp:1404) — dwCharID + the revival position.</summary>
    private static byte[] BuildRevivalAck(uint charId, float x, float y, float z)
    {
        var w = new PacketWriter(Msg.CS_REVIVAL_ACK, capacity: 20);
        w.WriteUInt32(charId);
        w.WriteFloat(x); w.WriteFloat(y); w.WriteFloat(z);
        return w.ToArray();
    }
}
