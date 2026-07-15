using TMap.Protocol;
using TMap.Server.Map;
using Xunit;

namespace TMap.Server.Tests;

public class MovementChatTests
{
    [Fact]
    public async Task Move_WithinCell_BroadcastsToNeighbour_NotToSelf()
    {
        var h = new MapTestHarness();
        var (sa, ca) = await h.EnterAsync(1, 1, 1, "Alice", mapId: 0); // both at the spawn cell (3663,557)
        var (sb, cb) = await h.EnterAsync(2, 2, 2, "Bob", mapId: 0);
        ca.Clear(); cb.Clear();

        // A short step that stays inside the same 64-unit cell: no enter/leave, just a move broadcast.
        await h.Service.DispatchClientAsync(sa, MapTestHarness.MoveReq(0, 3693f, 0f, 557f, dir: 900, speed: 3.0f));

        var move = cb.Last(Msg.CS_MOVE_ACK);
        Assert.NotNull(move);
        var r = new PacketReader(move!);
        Assert.Equal(1u, r.ReadUInt32());   // Alice's charId
        Assert.Equal(3693f, r.ReadFloat());
        Assert.Equal(0f, r.ReadFloat());
        Assert.Equal(557f, r.ReadFloat());
        r.ReadUInt16(); // pitch
        Assert.Equal((ushort)900, r.ReadUInt16()); // dir
        r.ReadByte(); r.ReadByte(); r.ReadByte();   // mouse/key/action
        Assert.Equal(3.0f, r.ReadFloat());  // speed

        Assert.False(ca.Has(Msg.CS_MOVE_ACK)); // no echo to self
        Assert.False(cb.Has(Msg.CS_LEAVE_ACK)); // stayed in view — no leave
        Assert.Equal(3693f, sa.Char!.PosX);
    }

    [Fact]
    public async Task Move_FarAway_LeavesNeighbourView_NoMove()
    {
        var h = new MapTestHarness();
        var (sa, ca) = await h.EnterAsync(1, 1, 1, "Alice", mapId: 0); // both at the spawn cell (57,8)
        var (sb, cb) = await h.EnterAsync(2, 2, 2, "Bob", mapId: 0);
        ca.Clear(); cb.Clear();

        // Move Alice many cells away: Bob drops out of her 3×3 view → both sides get a (cell-change) LEAVE.
        await h.Service.DispatchClientAsync(sa, MapTestHarness.MoveReq(0, 100f, 0f, 200f, dir: 0, speed: 3.0f));

        var leave = cb.Last(Msg.CS_LEAVE_ACK);
        Assert.NotNull(leave);
        var r = new PacketReader(leave!);
        Assert.Equal(1u, r.ReadUInt32());        // Alice's charId
        Assert.Equal((byte)0, r.ReadByte());     // exitMap = false (cell change, not a map exit)
        Assert.False(cb.Has(Msg.CS_MOVE_ACK));   // no longer in view → no move update
        Assert.True(ca.Has(Msg.CS_LEAVE_ACK));   // Alice is told Bob left her view too
    }

    [Fact]
    public async Task Move_TowardDistantPlayer_EntersView()
    {
        var h = new MapTestHarness();
        var (sa, ca) = await h.EnterAsync(1, 1, 1, "Alice", mapId: 0, x: 3663f, z: 557f); // cell (57,8)
        var (sb, cb) = await h.EnterAsync(2, 2, 2, "Bob", mapId: 0, x: 3840f, z: 557f);   // cell (60,8) — out of view
        Assert.False(cb.Has(Msg.CS_ENTER_ACK)); // not visible to each other on entry
        ca.Clear(); cb.Clear();

        // Alice steps to cell (59,8): Bob's cell (60) now falls inside her 3×3 → both get an ENTER.
        await h.Service.DispatchClientAsync(sa, MapTestHarness.MoveReq(0, 3776f, 0f, 557f, dir: 0, speed: 3.0f));

        var bobSeesAlice = cb.Last(Msg.CS_ENTER_ACK);
        Assert.NotNull(bobSeesAlice);
        Assert.Equal(1u, new PacketReader(bobSeesAlice!).ReadUInt32()); // Alice's charId

        var aliceSeesBob = ca.Last(Msg.CS_ENTER_ACK);
        Assert.NotNull(aliceSeesBob);
        Assert.Equal(2u, new PacketReader(aliceSeesBob!).ReadUInt32()); // Bob's charId

        Assert.True(cb.Has(Msg.CS_MOVE_ACK)); // and the move update follows the enter
    }

    [Fact]
    public async Task Move_NotVisibleAcrossDifferentMaps()
    {
        var h = new MapTestHarness();
        var (sa, ca) = await h.EnterAsync(1, 1, 1, "Alice", mapId: 0);
        var (sb, cb) = await h.EnterAsync(2, 2, 2, "Bob", mapId: 5);
        cb.Clear();

        await h.Service.DispatchClientAsync(sa, MapTestHarness.MoveReq(0, 10f, 0f, 20f, dir: 0, speed: 2f));

        Assert.False(cb.Has(Msg.CS_MOVE_ACK)); // different map → not a neighbour
    }

    [Fact]
    public async Task Move_SpeedHack_ClosesConnection()
    {
        var h = new MapTestHarness();
        var (sa, ca) = await h.EnterAsync(1, 1, 1, "Alice", mapId: 0);
        var (sb, cb) = await h.EnterAsync(2, 2, 2, "Bob", mapId: 0);
        cb.Clear();

        await h.Service.DispatchClientAsync(sa, MapTestHarness.MoveReq(0, 1f, 0f, 1f, dir: 0, speed: 99f));

        Assert.True(ca.Closed);
        Assert.False(cb.Has(Msg.CS_MOVE_ACK));
    }

    [Fact]
    public async Task Move_BeforeInGame_Ignored()
    {
        var h = new MapTestHarness();
        var client = new FakeClientChannel();
        var s = new ClientSession(client);
        await h.Service.DispatchClientAsync(s, MapTestHarness.ConnectReq(1, 1, 1)); // Entering, not InGame

        await h.Service.DispatchClientAsync(s, MapTestHarness.MoveReq(0, 1f, 0f, 1f, dir: 0, speed: 2f));
        Assert.False(client.Closed); // no crash, silently ignored
    }

    [Fact]
    public async Task Jump_EchoesToSelf_AndReachesNearNeighbour()
    {
        var h = new MapTestHarness();
        var (sa, ca) = await h.EnterAsync(1, 1, 1, "Alice", mapId: 0); // both at the spawn cell (same cell)
        var (sb, cb) = await h.EnterAsync(2, 2, 2, "Bob", mapId: 0);
        ca.Clear(); cb.Clear();

        await h.Service.DispatchClientAsync(sa, MapTestHarness.JumpReq(1, 0, 0, 3663f, 0f, 557f, dir: 10, action: 5));

        Assert.True(ca.Has(Msg.CS_JUMP_ACK)); // C++ GetNeerPlayer includes self → the mover gets its own jump echo
        Assert.True(cb.Has(Msg.CS_JUMP_ACK)); // a near neighbour (same cell) receives it
    }

    [Fact]
    public async Task Block_EchoesToSelf_AndReachesWholeNeighbourBlock()
    {
        var h = new MapTestHarness();
        var (sa, ca) = await h.EnterAsync(1, 1, 1, "Alice", mapId: 0, x: 640f, z: 640f); // cell (10,10)
        var (sb, cb) = await h.EnterAsync(2, 2, 2, "Bob", mapId: 0, x: 760f, z: 760f);   // cell (11,11): in 3×3, ~170 units away
        ca.Clear(); cb.Clear();

        await h.Service.DispatchClientAsync(sa, MapTestHarness.BlockReq(1, 0, 0, 640f, 0f, 640f, block: 1));

        Assert.True(ca.Has(Msg.CS_BLOCK_ACK)); // block echoes to self (C++ GetNeighbor, no self filter)
        Assert.True(cb.Has(Msg.CS_BLOCK_ACK)); // and reaches the whole 3×3 block — GetNeighbor has NO distance filter
    }

    [Fact]
    public async Task Jump_DistanceFilter_SkipsFarNeighbourInSameBlock()
    {
        var h = new MapTestHarness();
        var (sa, ca) = await h.EnterAsync(1, 1, 1, "Alice", mapId: 0, x: 640f, z: 640f); // cell (10,10)
        var (sb, cb) = await h.EnterAsync(2, 2, 2, "Bob", mapId: 0, x: 760f, z: 760f);   // cell (11,11): in 3×3, ~170 units away
        ca.Clear(); cb.Clear();

        await h.Service.DispatchClientAsync(sa, MapTestHarness.JumpReq(1, 0, 0, 640f, 0f, 640f));

        Assert.True(ca.Has(Msg.CS_JUMP_ACK));  // self echo
        Assert.False(cb.Has(Msg.CS_JUMP_ACK)); // Bob is in the 3×3 but >64 units away → GetNeerPlayer filters him out
    }

    [Fact]
    public async Task Enter_FlagsNewcomerAsNewMemberToExistingPlayers_ButNotViceVersa()
    {
        var h = new MapTestHarness();
        var (sa, ca) = await h.EnterAsync(1, 1, 1, "Alice", mapId: 0); // enters first
        ca.Clear();
        var (sb, cb) = await h.EnterAsync(2, 2, 2, "Bob", mapId: 0);   // Bob enters as a same-cell neighbour

        // Alice (existing) sees Bob (newcomer) → bNewMember == 1 (last byte of CS_ENTER_ACK).
        var aliceSeesBob = ca.Last(Msg.CS_ENTER_ACK);
        Assert.NotNull(aliceSeesBob);
        Assert.Equal((byte)1, aliceSeesBob![^1]);

        // Bob (newcomer) sees Alice (existing) → bNewMember == 0.
        var bobSeesAlice = cb.Last(Msg.CS_ENTER_ACK);
        Assert.NotNull(bobSeesAlice);
        Assert.Equal((byte)0, bobSeesAlice![^1]);
    }

    [Fact]
    public async Task ChatNear_BroadcastsToNeighbour_AndEchoesSender()
    {
        var h = new MapTestHarness();
        var (sa, ca) = await h.EnterAsync(1, 1, 1, "Alice", mapId: 0);
        var (sb, cb) = await h.EnterAsync(2, 2, 2, "Bob", mapId: 0);
        ca.Clear(); cb.Clear();

        await h.Service.DispatchClientAsync(sa, MapTestHarness.ChatReq("Alice", ChatGroup.Near, 0, "", "hello there"));

        foreach (var ch in new[] { ca, cb })
        {
            var pkt = ch.Last(Msg.CS_CHAT_ACK);
            Assert.NotNull(pkt);
            var r = new PacketReader(pkt!);
            Assert.Equal((byte)ChatGroup.Near, r.ReadByte());
            Assert.Equal(1u, r.ReadUInt32()); // sender charId
            Assert.Equal("Alice", r.ReadString());
            Assert.Equal("hello there", r.ReadString());
        }
    }

    [Fact]
    public async Task ChatWhisper_ByName_DeliversToTarget()
    {
        var h = new MapTestHarness();
        var (sa, ca) = await h.EnterAsync(1, 1, 1, "Alice", mapId: 0);
        var (sb, cb) = await h.EnterAsync(2, 2, 2, "Bob", mapId: 0);
        ca.Clear(); cb.Clear();

        await h.Service.DispatchClientAsync(sa, MapTestHarness.ChatReq("Alice", ChatGroup.Whisper, 0, "Bob", "psst"));

        Assert.True(cb.Has(Msg.CS_CHAT_ACK)); // delivered to Bob
        Assert.True(ca.Has(Msg.CS_CHAT_ACK)); // echoed to Alice
    }

    [Fact]
    public async Task ChatSpoofedSender_Dropped()
    {
        var h = new MapTestHarness();
        var (sa, ca) = await h.EnterAsync(1, 1, 1, "Alice", mapId: 0);
        var (sb, cb) = await h.EnterAsync(2, 2, 2, "Bob", mapId: 0);
        cb.Clear();

        await h.Service.DispatchClientAsync(sa, MapTestHarness.ChatReq("Mallory", ChatGroup.Near, 0, "", "spoof"));
        Assert.False(cb.Has(Msg.CS_CHAT_ACK));
    }

    [Fact]
    public async Task PartyChat_ForwardedToWorld()
    {
        var h = new MapTestHarness();
        await h.Service.OnWorldConnectedAsync();
        var (sa, ca) = await h.EnterAsync(1, 1, 1, "Alice", mapId: 0);
        h.World.Clear();

        await h.Service.DispatchClientAsync(sa, MapTestHarness.ChatReq("Alice", ChatGroup.Party, 0, "", "party!"));
        Assert.True(h.World.Has(Msg.MW_CHAT_ACK));
    }

    [Fact]
    public async Task Disconnect_BroadcastsLeaveToNeighbour()
    {
        var h = new MapTestHarness();
        var (sa, ca) = await h.EnterAsync(1, 1, 1, "Alice", mapId: 0);
        var (sb, cb) = await h.EnterAsync(2, 2, 2, "Bob", mapId: 0);
        cb.Clear();

        h.Service.OnClientDisconnect(sa);

        var leave = cb.Last(Msg.CS_LEAVE_ACK);
        Assert.NotNull(leave);
        var r = new PacketReader(leave!);
        Assert.Equal(1u, r.ReadUInt32());   // Alice's charId
        Assert.Equal((byte)1, r.ReadByte()); // exitMap
        Assert.Null(h.Service.State.FindByChar(1)); // de-registered
    }

    [Fact]
    public async Task Ping_EchoesTick()
    {
        var h = new MapTestHarness();
        var (sa, ca) = await h.EnterAsync(1, 1, 1, "Alice", mapId: 0);
        ca.Clear();

        var ping = new PacketWriter(Msg.CS_PINGMEASUREMENT_REQ).WriteUInt32(0xCAFE).ToArray();
        await h.Service.DispatchClientAsync(sa, ping);

        var ack = ca.Last(Msg.CS_PINGMEASUREMENT_ACK);
        Assert.NotNull(ack);
        Assert.Equal(0xCAFEu, new PacketReader(ack!).ReadUInt32());
    }
}
