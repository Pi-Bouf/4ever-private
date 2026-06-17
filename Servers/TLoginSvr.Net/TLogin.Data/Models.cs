namespace TLogin.Data;

/// <summary>Result of the TLogin stored procedure (and TLoginJP).</summary>
public sealed record LoginRow(
    int Ret,
    uint Key,
    uint CharId,
    uint UserId,
    string MapIp,
    ushort Port,
    byte CreateCnt,
    byte InPcBang,
    uint Premium);

/// <summary>Result of TRoute.</summary>
public sealed record RouteRow(int Ret, string Ip, ushort Port);

/// <summary>A row of TGROUP — identifies a world/group and its game database.</summary>
public sealed record GroupConfig(
    byte GroupId,
    byte Type,
    string Name,
    string Dsn,
    string DbUser,
    string DbPassword);

/// <summary>A server-group entry for CS_GROUPLIST_ACK (TGROUP joined with live counts).</summary>
public sealed record GroupListRow(
    byte GroupId,
    byte Type,
    string Name,
    byte Status,        // raw TGROUP.bStatus (TSVR_STATUS)
    ushort Full,
    ushort Busy,
    uint MaxUser,
    uint CurrentUsers,  // distinct online users
    byte CharCount);    // this account's character count in the group

/// <summary>A channel entry for CS_CHANNELLIST_ACK.</summary>
public sealed record ChannelRow(
    byte Channel,
    string Name,
    byte Status,        // raw TCHANNEL.bStatus
    ushort Full,
    ushort Busy,
    uint Count);

/// <summary>A character row from TCHARTABLE.</summary>
public sealed record CharRow(
    string Name,
    uint CharId,
    byte StartAct,
    byte Class,
    byte Race,
    byte Country,
    byte Sex,
    byte Hair,
    byte Face,
    byte Body,
    byte Pants,
    byte Hand,
    byte Foot,
    byte Slot,
    byte Level,
    uint Region,
    byte HelmetHide)
{
    public string GuildName { get; set; } = "";
    public uint Fame { get; set; }
    public uint FameColor { get; set; }
    public List<ItemRow> Items { get; } = new();
}

/// <summary>An equipped-item row from TITEMTABLE (column aliases map dwTime3/4/6 → color/regGuild/customTex).</summary>
public sealed record ItemRow(
    byte ItemId,
    ushort ItemTemplateId,
    byte Level,
    byte GradeEffect,
    ushort Color,
    byte RegGuild,
    ushort CustomTex,
    ushort MoggItemId);

/// <summary>Guild info from TGetGuildInfo.</summary>
public sealed record GuildRow(string Name, uint Fame, uint FameColor);

/// <summary>Result of TFindServerID.</summary>
public sealed record FindServerRow(int Ret, byte ServerId);

/// <summary>Result of TCreateChar — only RET, the new char id and the create-count are OUTPUT params;
/// the appearance fields are echoed from the request by the handler.</summary>
public sealed record CreateCharRow(int Ret, uint CharId, byte CreateCnt);

/// <summary>Result of TDeleteChar.</summary>
public sealed record DeleteCharRow(int Ret, byte CreateCnt);

/// <summary>Arguments to TCreateChar.</summary>
public sealed record CreateCharArgs(
    string Name,
    uint UserId,
    byte Group,
    byte Slot,
    byte Class,
    byte Race,
    byte Country,
    byte Sex,
    byte Hair,
    byte Face,
    byte Body,
    byte Pants,
    byte Hand,
    byte Foot,
    byte LevelOption);

/// <summary>A row of TVETERANCHART (bID → option, bLevel).</summary>
public sealed record VeteranRow(byte Option, byte Level);
