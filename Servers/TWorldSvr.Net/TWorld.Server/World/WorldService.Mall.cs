using TWorld.Protocol;
using TWorld.Server.Net;

namespace TWorld.Server.World;

/// <summary>
/// Final MW slice — guild treasury / cash-mall economy, ported from <c>SSHandler.cpp</c>.
/// <list type="bullet">
/// <item><c>GUILDPOINTREWARD</c>: a chief spends the guild's useable PvP points to reward a member, logging it
/// and forwarding the gain to the member's main.</item>
/// <item><c>MONSTERBUY</c>: a purchase from the guild monster shop, paid out of the guild treasury.</item>
/// <item><c>CASHITEMSALE</c>: a map acknowledges a cash-item sale; the sale is persisted once every map has.</item>
/// <item><c>CMGIFT</c> / <c>CMGIFTRESULT</c>: cash-mall gift delivery and its result routing.</item>
/// </list>
/// The cash-mall gift catalog (and the cash-sale catalog) are fed by the un-ported control plane, so they're
/// empty at runtime; the gift take-check / sale-record persistence rides the un-ported DM plane and is
/// flagged as a deferred gap where it would fire.
/// </summary>
public sealed partial class WorldService
{
    private async Task<bool> DispatchMallAsync(ServerSession session, PacketReader r)
    {
        switch (r.Id)
        {
            case Msg.MW_GUILDPOINTREWARD_ACK: OnMW_GUILDPOINTREWARD_ACK(session, r); return true;
            case Msg.MW_MONSTERBUY_ACK: OnMW_MONSTERBUY_ACK(session, r); return true;
            case Msg.MW_CASHITEMSALE_ACK: OnMW_CASHITEMSALE_ACK(session, r); return true;
            case Msg.MW_CMGIFT_ACK: await OnMW_CMGIFT_ACK(r); return true;
            // CMGIFTRESULT_REQ and _ACK share an id; an inbound one is always the map's ACK.
            case Msg.MW_CMGIFTRESULT_ACK: OnMW_CMGIFTRESULT_ACK(r); return true;
        }
        return false;
    }

    /// <summary>A guild chief grants useable PvP points to a member as a reward. C++ OnMW_GUILDPOINTREWARD_ACK.</summary>
    private void OnMW_GUILDPOINTREWARD_ACK(ServerSession session, PacketReader r)
    {
        uint charId = r.ReadUInt32();
        uint key = r.ReadUInt32();
        string target = r.ReadString();
        uint point = r.ReadUInt32();
        string message = r.ReadString();

        var master = _state.FindChar(charId, key);
        if (master?.Guild is not { } guild) return;
        if (guild.Chief != charId) return;

        if (guild.PvPUseablePoint < point)
        {
            session.Send(BuildGuildPointReward((byte)GprResult.NeedPoint, charId, key, guild.PvPUseablePoint));
            return;
        }

        var member = guild.FindMember(target);
        if (member is null || member.OnlineChar is { Save: false })
        {
            session.Send(BuildGuildPointReward((byte)GprResult.NoMember, charId, key, guild.PvPUseablePoint));
            return;
        }

        uint targetId = member.CharId;
        guild.UsePvPoint(point, Proto.PvpUseable);
        guild.PointLog(point, target, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        if (_guildDb is not null)
            _ = _guildDb.SaveGuildPointRewardAsync(guild.Id, point, target, guild.PvPTotalPoint, guild.PvPUseablePoint);

        session.Send(BuildGuildPointReward((byte)GprResult.Success, charId, key, guild.PvPUseablePoint, point, targetId, target, message));

        if (member.OnlineChar is { } online)
        {
            var w = new PacketWriter(Msg.MW_GAINPVPPOINT_REQ);
            w.WriteUInt32(targetId); w.WriteUInt32(point); w.WriteByte(Proto.PvpeGuild); w.WriteByte(Proto.PvpUseable);
            w.WriteByte(1); w.WriteString(""); w.WriteByte(0); w.WriteByte(0);
            _state.FindMapSvr(online.MainId)?.Send(w.ToArray());
        }
    }

    /// <summary>Buy from the guild monster shop, paid from the treasury. C++ OnMW_MONSTERBUY_ACK.</summary>
    private void OnMW_MONSTERBUY_ACK(ServerSession session, PacketReader r)
    {
        uint charId = r.ReadUInt32();
        uint key = r.ReadUInt32();
        ushort npcId = r.ReadUInt16();
        ushort id = r.ReadUInt16();
        uint price = r.ReadUInt32();

        var ch = _state.FindChar(charId, key);
        if (ch?.Guild is not { } guild) return;

        bool paid = guild.UseMoney(price, use: true);
        if (paid && price != 0 && _guildDb is not null)
            _ = _guildDb.ContributionAsync(guild.Id, 0, guild.Exp, guild.Gold, guild.Silver, guild.Cooper);

        byte result = paid ? (byte)MonsterBuyResult.Success : (byte)MonsterBuyResult.NeedMoney;
        var w = new PacketWriter(Msg.MW_MONSTERBUY_REQ);
        w.WriteByte(result); w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteUInt32(guild.Id);
        w.WriteUInt16(npcId); w.WriteUInt16(id); w.WriteUInt32(price);
        session.Send(w.ToArray());
    }

    /// <summary>A map acknowledges receiving the current cash-item sale. The sale only persists once every
    /// connected map has confirmed. C++ OnMW_CASHITEMSALE_ACK.</summary>
    private void OnMW_CASHITEMSALE_ACK(ServerSession session, PacketReader r)
    {
        uint index = r.ReadUInt32();
        _ = r.ReadUInt16();   // value
        _ = r.ReadByte();     // ret

        session.CashSale = true;

        bool allConfirmed = _state.Servers.Values.All(s => s.CashSale);
        if (allConfirmed && _state.CashItemSales.TryGetValue(index, out var ev))
        {
            // C++ OnMW_CASHITEMSALE_ACK: once every map confirmed, persist each item's sale value (CSPCashItemSale).
            foreach (var it in ev.Items)
                _ = PersistGame(() => _gameDb!.CashItemSaleAsync(it.Id, it.SaleValue), "TCashItemSale");
        }
    }

    /// <summary>A character takes a cash-mall gift. C++ OnMW_CMGIFT_ACK → (DB take-check) → OnDM_CMGIFT_ACK.
    /// A gift with TakeType!=0 runs the DB take-check (CSPCMGiftCanTake) inline before routing.</summary>
    private async Task OnMW_CMGIFT_ACK(PacketReader r)
    {
        string target = r.ReadString();
        ushort giftId = r.ReadUInt16();
        uint gmCharId = r.ReadUInt32();

        if (_state.CmGifts.TryGetValue(giftId, out var gift))
        {
            byte ret = gift.TakeType != 0 ? await CmGiftTakeCheckAsync(target, giftId) : (byte)CmGiftResult.Success;
            RouteCmGift(ret, target, giftId, tool: 0, gmCharId);
            return;
        }
        RouteCmGift((byte)CmGiftResult.Id, target, giftId, tool: 0, gmCharId);
    }

    /// <summary>The DB take-check for a gift with TakeType!=0 (CSPCMGiftCanTake). Mirrors the C++ default of
    /// CMGIFT_FAIL when the query can't run (no DB configured).</summary>
    private async Task<byte> CmGiftTakeCheckAsync(string target, ushort giftId)
    {
        if (_gameDb is null) return (byte)CmGiftResult.Fail;
        try { return await _gameDb.CmGiftCanTakeAsync(target, giftId); }
        catch (Exception ex) { _log.LogWarning(ex, "TCMGiftCanTake failed."); return (byte)CmGiftResult.Fail; }
    }

    /// <summary>The result of a gift delivery, relayed back to the GM (or the control tool). C++ OnMW_CMGIFTRESULT_ACK.</summary>
    private void OnMW_CMGIFTRESULT_ACK(PacketReader r)
    {
        byte ret = r.ReadByte();
        byte tool = r.ReadByte();
        uint gmId = r.ReadUInt32();
        SendCmGiftResult(ret, tool, gmId);
    }

    // ===== helpers =====

    /// <summary>Best-effort game-DB persistence (mirrors the C++ fire-and-forget DM-thread writes): no-op when
    /// no game DB is configured, and a failure (e.g. a proc absent from the baseline) is logged, not fatal.</summary>
    private async Task PersistGame(Func<Task> op, string what)
    {
        if (_gameDb is null) return;
        try { await op(); } catch (Exception ex) { _log.LogWarning(ex, "{Proc} failed.", what); }
    }

    private static byte[] BuildGuildPointReward(byte result, uint charId, uint key, uint remainPoint,
        uint point = 0, uint targetId = 0, string target = "", string message = "")
    {
        var w = new PacketWriter(Msg.MW_GUILDPOINTREWARD_REQ);
        w.WriteByte(result); w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteUInt32(remainPoint);
        w.WriteUInt32(point); w.WriteUInt32(targetId); w.WriteString(target); w.WriteString(message);
        return w.ToArray();
    }

    /// <summary>Route a gift outcome: on success/duplicate deliver to the target's map, else relay the result
    /// back to the GM/tool. Full port of C++ OnDM_CMGIFT_ACK, including the CMGIFT_DUPLICATE → errGift refetch
    /// (→ CMGIFT_ERRPOST) path that was previously deferred to the DM plane.</summary>
    private void RouteCmGift(byte result, string target, ushort giftId, byte tool, uint gmId)
    {
        if (result == (byte)CmGiftResult.Success || result == (byte)CmGiftResult.Duplicate)
        {
            if (_state.CmGifts.TryGetValue(giftId, out var gift))
            {
                if (result == (byte)CmGiftResult.Duplicate)
                {
                    // The gift was already taken: fall back to its configured error gift, if any.
                    if (!_state.CmGifts.TryGetValue(gift.ErrGiftId, out var errGift))
                    {
                        SendCmGiftResult(result, tool, gmId);
                        return;
                    }
                    gift = errGift;
                    result = (byte)CmGiftResult.ErrPost;
                }

                if (gift.ToolOnly <= tool)
                {
                    if (_state.CharactersByName.TryGetValue(target, out var tch))
                    {
                        var targetMap = _state.FindMapSvr(tch.MainId);
                        if (targetMap is not null)
                        {
                            var w = new PacketWriter(Msg.MW_CMGIFT_REQ);
                            w.WriteUInt32(tch.CharId); w.WriteByte(gift.GiftType); w.WriteUInt32(gift.Value); w.WriteByte(gift.Count);
                            w.WriteString(gift.Title); w.WriteString(gift.Msg); w.WriteUInt32(gmId); w.WriteByte(tool);
                            w.WriteUInt16(giftId); w.WriteUInt16(gift.GiftId); w.WriteByte(result);
                            targetMap.Send(w.ToArray());
                            return;
                        }
                        result = (byte)CmGiftResult.Fail;
                    }
                    else result = (byte)CmGiftResult.Target;
                }
                else result = (byte)CmGiftResult.Fail;
            }
            else result = (byte)CmGiftResult.Id;
        }
        SendCmGiftResult(result, tool, gmId);
    }

    /// <summary>Route a gift result to the control tool (CT) or the GM's map (MW). C++ OnDM_CMGIFT_ACK tail.</summary>
    private void SendCmGiftResult(byte result, byte tool, uint gmId)
    {
        if (tool != 0)
        {
            var w = new PacketWriter(Msg.CT_CMGIFT_ACK);
            w.WriteByte(result); w.WriteUInt32(gmId);
            _state.ControlServer?.Send(w.ToArray());
            return;
        }
        if (_state.Characters.TryGetValue(gmId, out var gm) && _state.FindMapSvr(gm.MainId) is { } gmMap)
        {
            var w = new PacketWriter(Msg.MW_CMGIFTRESULT_REQ);
            w.WriteByte(result); w.WriteUInt32(gmId);
            gmMap.Send(w.ToArray());
        }
    }
}
