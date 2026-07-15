using Microsoft.Extensions.Logging;
using TMap.Protocol;

namespace TMap.Server.Map;

/// <summary>
/// Chat — ported from <c>OnCS_CHAT_REQ</c> (CSHandler.cpp:5206) / <c>SendCS_CHAT_ACK</c>. Local scopes
/// (CHAT_NEAR, and CHAT_WHISPER when the target is on this map) are delivered directly; the remaining
/// scopes (party/guild/tactics/map/force and unresolved whispers) are forwarded to the world via
/// <c>MW_CHAT_ACK</c>. Delivery of world-relayed chat back to clients (<c>OnMW_CHAT_REQ</c>) is a
/// documented Phase-1 deferral — see PORT_STATUS.md.
/// </summary>
public sealed partial class MapService
{
    private const int MaxChatLen = 1024;

    private void OnCS_CHAT_REQ(ClientSession s, PacketReader r)
    {
        if (s.State != EnterState.InGame || s.Char is not { } ch) return;

        string sender = r.ReadString();
        var group = (ChatGroup)r.ReadByte();
        uint target = r.ReadUInt32();
        string name = r.ReadString();
        string talk = r.ReadString();

        if (group is ChatGroup.World or ChatGroup.Operator) return; // clients may not originate these
        if (talk.Length > MaxChatLen) talk = talk[..MaxChatLen];

        // Anti-spoof: the sender name must match the char (the C++ chat-bans a mismatch). Phase-1 just drops.
        if (!string.Equals(sender, ch.Name, StringComparison.Ordinal))
        {
            _log.LogWarning("Chat sender spoof from char {Char} ('{Sender}' != '{Name}').", s.CharId, sender, ch.Name);
            return;
        }

        switch (group)
        {
            case ChatGroup.Near:
                foreach (var other in _state.Neighbors(s))
                    other.Send(BuildCS_CHAT_ACK(group, s.CharId, ch.Name, talk));
                s.Send(BuildCS_CHAT_ACK(group, s.CharId, ch.Name, talk)); // echo to self
                break;

            case ChatGroup.Whisper:
                var to = FindByName(name);
                if (to is not null)
                {
                    to.Send(BuildCS_CHAT_ACK(group, s.CharId, ch.Name, talk));
                    s.Send(BuildCS_CHAT_ACK(group, s.CharId, ch.Name, talk)); // echo to sender
                }
                else
                {
                    SendMW_CHAT_ACK(s, (byte)group, target, name, talk); // maybe on another map
                }
                break;

            default:
                SendMW_CHAT_ACK(s, (byte)group, target, name, talk);
                break;
        }
    }

    private static byte[] BuildCS_CHAT_ACK(ChatGroup group, uint senderId, string name, string talk)
    {
        var w = new PacketWriter(Msg.CS_CHAT_ACK);
        w.WriteByte((byte)group);
        w.WriteUInt32(senderId);
        w.WriteString(name);
        w.WriteString(talk);
        return w.ToArray();
    }

    private void SendMW_CHAT_ACK(ClientSession s, byte group, uint target, string name, string talk)
    {
        if (!_worldReady) return;
        var w = new PacketWriter(Msg.MW_CHAT_ACK);
        w.WriteByte(s.Channel);
        w.WriteUInt32(s.CharId);
        w.WriteUInt32(s.Key);
        w.WriteString(s.Char!.Name);
        w.WriteByte(group);   // bType
        w.WriteByte(group);   // bGroup
        w.WriteUInt32(target);
        w.WriteString(name);
        w.WriteString(talk);
        _world.Send(w);
    }

    private void OnMW_CHAT_REQ(PacketReader r)
    {
        // World-relayed chat delivery to local clients is deferred (the exact MW_CHAT_REQ payload layout is
        // not yet transcribed). Local NEAR/WHISPER chat already works end-to-end. See PORT_STATUS.md.
        _log.LogDebug("MW_CHAT_REQ received (world-relayed chat delivery deferred).");
    }

    private ClientSession? FindByName(string name)
    {
        foreach (var s in _state.AllInGame())
            if (string.Equals(s.Char!.Name, name, StringComparison.Ordinal))
                return s;
        return null;
    }
}
