using TPatch.Data;
using TPatch.Protocol;
using Xunit;

namespace TPatch.Server.Tests;

/// <summary>
/// End-to-end handler tests over the real net path. Reply layouts are asserted field-by-field in the exact
/// order the launcher reads them (<c>Tools/TLauncher/4StoryDlg.cpp::OnCT_NEWPATCH_ACK</c>) and the C++
/// server writes them (<c>TPatchSvr/Sender.cpp</c>).
/// </summary>
public class PatchFlowTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task NewPatch_Returns_MinBeta_LoginEndpoint_And_Files()
    {
        var src = new FakePatchSource { MinBetaVer = 42 };
        src.Versions.Add(new PatchFile(101, "sub/", "a.dat", 1000, 0));
        src.Versions.Add(new PatchFile(102, "", "b.dat", 2000, 5));

        await using var host = new PatchTestHost(src, new PatchServerOptions { FtpUrl = "http://test/patch" });
        using var c = await host.ConnectAsync();

        var req = new PacketWriter(Msg.CT_NEWPATCH_REQ);
        req.WriteUInt32(100); // client's current version
        c.Send(req);

        var r = c.Receive(Timeout);
        Assert.Equal(Msg.CT_NEWPATCH_ACK, r.Id);
        Assert.Equal("http://test/patch", r.ReadString());
        Assert.Equal(PatchTestHost.LoginIp, r.ReadUInt32());
        Assert.Equal(PatchTestHost.LoginPort, r.ReadUInt16());
        Assert.Equal(42u, r.ReadUInt32());
        Assert.Equal((ushort)2, r.ReadUInt16());

        Assert.Equal(101u, r.ReadUInt32());
        Assert.Equal("sub/", r.ReadString());
        Assert.Equal("a.dat", r.ReadString());
        Assert.Equal(1000u, r.ReadUInt32());
        Assert.Equal(0u, r.ReadUInt32());

        Assert.Equal(102u, r.ReadUInt32());
        Assert.Equal("", r.ReadString());
        Assert.Equal("b.dat", r.ReadString());
        Assert.Equal(2000u, r.ReadUInt32());
        Assert.Equal(5u, r.ReadUInt32());
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public async Task Patch_Returns_Older_List_Without_Beta()
    {
        var src = new FakePatchSource();
        src.Versions.Add(new PatchFile(10, "p/", "x.dat", 111, 0));

        await using var host = new PatchTestHost(src, new PatchServerOptions { FtpUrl = "http://test/patch" });
        using var c = await host.ConnectAsync();

        var req = new PacketWriter(Msg.CT_PATCH_REQ);
        req.WriteUInt32(0);
        c.Send(req);

        var r = c.Receive(Timeout);
        Assert.Equal(Msg.CT_PATCH_ACK, r.Id);
        Assert.Equal("http://test/patch", r.ReadString());
        Assert.Equal(PatchTestHost.LoginIp, r.ReadUInt32());
        Assert.Equal(PatchTestHost.LoginPort, r.ReadUInt16());
        Assert.Equal((ushort)1, r.ReadUInt16());
        Assert.Equal(10u, r.ReadUInt32());
        Assert.Equal("p/", r.ReadString());
        Assert.Equal("x.dat", r.ReadString());
        Assert.Equal(111u, r.ReadUInt32());
        Assert.Equal(0, r.Remaining); // no per-file beta field
    }

    [Fact]
    public async Task PrePatch_Returns_Beta_List()
    {
        var src = new FakePatchSource();
        src.PreVersions.Add(new PatchFile(0, "beta/", "p.dat", 500, 7));

        await using var host = new PatchTestHost(src, new PatchServerOptions { PreFtpUrl = "http://test/pre" });
        using var c = await host.ConnectAsync();

        var req = new PacketWriter(Msg.CT_PREPATCH_REQ);
        req.WriteUInt32(6);
        c.Send(req);

        var r = c.Receive(Timeout);
        Assert.Equal(Msg.CT_PREPATCH_ACK, r.Id);
        Assert.Equal("http://test/pre", r.ReadString());
        Assert.Equal(PatchTestHost.LoginIp, r.ReadUInt32());
        Assert.Equal(PatchTestHost.LoginPort, r.ReadUInt16());
        Assert.Equal((ushort)1, r.ReadUInt16());
        Assert.Equal(7u, r.ReadUInt32());       // betaVer is written FIRST for prepatch
        Assert.Equal("beta/", r.ReadString());
        Assert.Equal("p.dat", r.ReadString());
        Assert.Equal(500u, r.ReadUInt32());
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public async Task ChangeIf_Returns_Interface_List_With_Interface_Ftp()
    {
        var src = new FakePatchSource();
        src.Interface.Add(new PatchFile(500, "", "ui.dat", 300, 0));

        await using var host = new PatchTestHost(src, new PatchServerOptions { FtpUrl = "http://test/patch" });
        using var c = await host.ConnectAsync();

        var req = new PacketWriter(Msg.CT_CHANGEIF_REQ);
        req.WriteByte(1); // bOption
        c.Send(req);

        var r = c.Receive(Timeout);
        Assert.Equal(Msg.CT_NEWPATCH_ACK, r.Id);
        Assert.Equal("http://test/patch/interface", r.ReadString());
        Assert.Equal(PatchTestHost.LoginIp, r.ReadUInt32());
        Assert.Equal(PatchTestHost.LoginPort, r.ReadUInt16());
        Assert.Equal(0u, r.ReadUInt32()); // min beta = 0 for interface
        Assert.Equal((ushort)1, r.ReadUInt16());
        Assert.Equal(500u, r.ReadUInt32());
        Assert.Equal("", r.ReadString());   // path always empty
        Assert.Equal("ui.dat", r.ReadString());
        Assert.Equal(300u, r.ReadUInt32());
        Assert.Equal(0u, r.ReadUInt32());    // beta always 0
    }

    [Fact]
    public async Task Monitor_Replies_And_Echoes_Tick()
    {
        await using var host = new PatchTestHost(new FakePatchSource());
        using var c = await host.ConnectAsync();

        var req = new PacketWriter(Msg.CT_SERVICEMONITOR_ACK);
        req.WriteInt64(0);          // leading INT64 (skipped server-side)
        req.WriteUInt32(0x99);      // tick
        c.Send(req);

        var r = c.Receive(Timeout);
        Assert.Equal(Msg.CT_SERVICEMONITOR_REQ, r.Id);
        Assert.Equal(0L, r.ReadInt64());
        Assert.Equal(0x99u, r.ReadUInt32());
        Assert.True(r.ReadUInt32() >= 1);  // curSession
        Assert.True(r.ReadUInt32() >= 1);  // curUser
        Assert.Equal(0u, r.ReadUInt32());  // activeUser
    }

    [Fact]
    public async Task PatchStart_Closes_The_Connection()
    {
        await using var host = new PatchTestHost(new FakePatchSource());
        using var c = await host.ConnectAsync();

        c.Send(new PacketWriter(Msg.CT_PATCHSTART_REQ));
        Assert.Throws<IOException>(() => c.Receive(Timeout));
    }

    [Fact]
    public async Task PrePatchComplete_Writes_Db_Then_Closes()
    {
        var src = new FakePatchSource();
        await using var host = new PatchTestHost(src);
        using var c = await host.ConnectAsync();

        var req = new PacketWriter(Msg.CT_PREPATCHCOMPLETE_REQ);
        req.WriteUInt32(9);
        c.Send(req);

        Assert.Throws<IOException>(() => c.Receive(Timeout));
        Assert.Contains(9u, src.PreCompleteCalls);
    }

    [Fact]
    public async Task Unknown_Packet_Closes_The_Connection()
    {
        await using var host = new PatchTestHost(new FakePatchSource());
        using var c = await host.ConnectAsync();

        c.Send(new PacketWriter(0xFFFF));
        Assert.Throws<IOException>(() => c.Receive(Timeout));
    }
}
