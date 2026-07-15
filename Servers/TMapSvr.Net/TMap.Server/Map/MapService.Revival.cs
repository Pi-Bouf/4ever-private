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
/// <para><b>Deferred (documented — PORT_STATUS.md):</b> the death penalty (<c>SetAftermath</c> — the
/// aftermath stat reduction is a buff subsystem, already stubbed to 0 in the stat sheet), <c>RespawnCompanion</c>
/// (pets), the <c>TREVIVAL_SKILL</c> revival-protection buff (<c>ForceMaintain</c>), the <c>REVIVAL_HELP</c>
/// priest-resurrection ask flow (<c>CS_REVIVALASK</c>/<c>CS_REVIVALREPLY</c>), and BoW/BR respawn placement.
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

        // Restore HP/MP by type (C++ Revival: ATONCE/NPC 30%, GHOST 40%; DWORD truncation; HP ≥ 1). Back to NORMAL.
        ch.Mode = MtNormal;
        ch.Action = 0; // TA_STAND
        uint maxHp = MaxHpFor(ch), maxMp = MaxMpFor(ch);
        double frac = type == (byte)RevivalType.Npc ? 0.3 : 0.4;
        ch.Hp = Math.Max(1u, (uint)(maxHp * frac));
        ch.Mp = (uint)(maxMp * frac);
        ch.RecoverHpTick = NowMs; ch.RecoverMpTick = NowMs; // regen resumes cleanly from now

        // Broadcast the revival + the restored bar to the 3×3 view (incl. self — C++ GetNeighbor).
        var reviveAck = BuildRevivalAck(ch.CharId, ch.PosX, ch.PosY, ch.PosZ);
        foreach (var p in _state.InView(s))
        {
            p.Send(reviveAck);
            SendSelfHpMp(p, ch.CharId, maxHp, ch.Hp, maxMp, ch.Mp);
        }
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
