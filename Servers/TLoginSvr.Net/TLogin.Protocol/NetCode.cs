namespace TLogin.Protocol;

/// <summary>
/// Message IDs for the client↔login protocol. Values come from THIS repo's
/// <c>Lib/Own/TProtocol/include/ProtocolBase.h</c> (<c>CS_LOGIN = 0x1987</c>, <c>CS_MAP = 0x5280</c>)
/// and <c>CSProtocol.h</c> offsets. They differ from the 4retro fork's IDs.
/// </summary>
public static class Msg
{
    public const ushort CS_LOGIN = 0x1987;
    public const ushort CS_MAP = 0x5280;

    public const ushort CS_LOGIN_REQ = CS_LOGIN + 0x0001;
    public const ushort CS_LOGIN_ACK = CS_LOGIN + 0x0002;
    public const ushort CS_GROUPLIST_REQ = CS_LOGIN + 0x0003;
    public const ushort CS_GROUPLIST_ACK = CS_LOGIN + 0x0004;
    public const ushort CS_CHANNELLIST_REQ = CS_LOGIN + 0x0005;
    public const ushort CS_CHANNELLIST_ACK = CS_LOGIN + 0x0006;
    public const ushort CS_CHARLIST_REQ = CS_LOGIN + 0x0007;
    public const ushort CS_CHARLIST_ACK = CS_LOGIN + 0x0008;
    public const ushort CS_CREATECHAR_REQ = CS_LOGIN + 0x0009;
    public const ushort CS_CREATECHAR_ACK = CS_LOGIN + 0x000A;
    public const ushort CS_DELCHAR_REQ = CS_LOGIN + 0x000B;
    public const ushort CS_DELCHAR_ACK = CS_LOGIN + 0x000C;
    public const ushort CS_START_REQ = CS_LOGIN + 0x000D;
    public const ushort CS_START_ACK = CS_LOGIN + 0x000E;
    public const ushort CS_TESTLOGIN_REQ = CS_LOGIN + 0x000F;
    public const ushort CS_TESTVERSION_REQ = CS_LOGIN + 0x0011;
    public const ushort CS_TESTVERSION_ACK = CS_LOGIN + 0x0012;
    public const ushort CS_AGREEMENT_REQ = CS_LOGIN + 0x0013;
    public const ushort CS_HOTSEND_REQ = CS_LOGIN + 0x0014;
    public const ushort CS_HOTSEND_ACK = CS_LOGIN + 0x0015;
    public const ushort CS_VETERAN_REQ = CS_LOGIN + 0x0016;
    public const ushort CS_VETERAN_ACK = CS_LOGIN + 0x0017;
    public const ushort CS_SECURITYCONFIRM_REQ = CS_LOGIN + 0x0018;
    public const ushort CS_SECURITYCONFIRM_ACK = CS_LOGIN + 0x0019;
    public const ushort CS_SECURITYRESULT_ACK = CS_LOGIN + 0x0020;

    public const ushort CS_TERMINATE_REQ = CS_MAP + 0x01D2;
    public const ushort CS_BOWPLAYERNOTIFY_ACK = CS_MAP + 0x0373;
}

// enum TLOGIN_RESULT (NetCode.h)
public enum LoginResult : byte
{
    Success = 0,
    NoUser,
    InvalidPasswd,
    Duplicate,
    Version,
    Internal,
    Block,
    IpBlock,
    NeedAgreement,
    NeedWorldUnify,
    Security,
    PwChange,
}

// enum TCREATECHAR_RESULT
public enum CreateResult : byte
{
    Success = 0,
    NoGroup,
    DupName,
    InvalidSlot,
    Protected,
    OverChar,
    NeedCard,
    Internal,
}

// enum TDELCHAR_RESULT
public enum DeleteResult : byte
{
    Success = 0,
    InvalidPasswd,
    NoGroup,
    Internal,
    Guild,
}

// enum TSTART_RESULT
public enum StartResult : byte
{
    Success = 0,
    NoServer,
    NoGroup,
    Internal,
}

// enum NATION_TYPE
public enum Nation : byte
{
    None = 0,
    Korea,
    German,
    Us,
    Japan,
    Taiwan,
    Russia,
}

// enum TSTATUS_TYPE (sent to client)
public enum ServerStatus : byte
{
    Sleep = 0,
    Normal,
    Busy,
    Full,
}

// enum TSVR_STATUS (stored in DB)
public enum SvrStatus : byte
{
    Disable = 0,
    Enable,
    Sleep,
}

// enum (security result, CODE_CORRECT/CODE_INCORRECT)
public enum SecurityCode : byte
{
    Correct = 0,
    Incorrect,
}

/// <summary>Assorted protocol constants from THIS repo's headers.</summary>
public static class Proto
{
    public const ushort ClientVersion = 0x2918; // TVERSION

    public const byte SvrGrpMapSvr = 4;   // SVRGRP_MAPSVR
    public const byte BowServerId = 30;   // BOW_SERVER_ID
    public const byte BrServerId = 50;    // BR_SERVER_ID

    public const int MaxName = 50;        // MAX_NAME

    // CTBLItem query parameters for equipped gear.
    public const byte StorageInven = 0;   // STORAGE_INVEN
    public const byte InvenEquip = 0xFE;  // INVEN_EQUIP
    public const byte OwnerChar = 0;      // TOWNER_CHAR

    // The anti-tamper key used by OnCS_LOGIN_REQ's version-checksum validation.
    public const long LoginChecksumKey = 0x336c3aebf71a8b08;

    /// <summary>Converts a dotted IPv4 string to the little-endian uint produced by inet_addr.</summary>
    public static uint IpToUInt(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return 0;
        var parts = ip.Split('.');
        if (parts.Length != 4) return 0;
        uint result = 0;
        for (int i = 0; i < 4; i++)
        {
            if (!byte.TryParse(parts[i], out byte b)) return 0;
            result |= (uint)b << (8 * i); // inet_addr packs the first octet into the low byte
        }
        return result;
    }

    /// <summary>
    /// Recomputes the version checksum the client must send in CS_LOGIN_REQ
    /// (see OnCS_LOGIN_REQ in CSHandler.cpp).
    /// </summary>
    public static long ComputeLoginChecksum(ushort version)
    {
        long checksum = version * 2 - 500;
        long index = checksum % 8;
        long body = checksum / 8;
        for (long i = 0; i < index; i++)
        {
            checksum ^= body;
            checksum += LoginChecksumKey;
        }
        return checksum;
    }
}
