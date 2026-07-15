namespace TPatch.Protocol;

/// <summary>
/// Patch-plane and control-plane message IDs handled by TPatchSvr. Values are computed from the plane
/// bases in this repo's <c>Lib/Own/TProtocol/include/ProtocolBase.h</c> (<c>CT_PATCH = 0x4201</c>,
/// <c>CT_CONTROL = 0x9301</c>) and the offsets in <c>CTProtocol.h</c>. Cross-checked against the launcher
/// (<c>Tools/TLauncher/4StoryDlg.cpp</c>) and the C++ handler switch (<c>TPatchSvr/TPatchSvr.cpp</c>).
/// </summary>
public static class Msg
{
    public const ushort CT_PATCH = 0x4201;   // patch server ↔ client (launcher)
    public const ushort CT_CONTROL = 0x9301; // control server ↔ patch server

    // ---- CT_PATCH plane ----
    public const ushort CT_PATCH_REQ = CT_PATCH + 0x0001;            // 0x4202 (older client) → CT_PATCH_ACK
    public const ushort CT_PATCH_ACK = CT_PATCH + 0x0002;            // 0x4203
    public const ushort CT_PATCHSTART_REQ = CT_PATCH + 0x0005;       // 0x4206 → close session
    public const ushort CT_PREPATCH_REQ = CT_PATCH + 0x0006;         // 0x4207 → CT_PREPATCH_ACK
    public const ushort CT_PREPATCH_ACK = CT_PATCH + 0x0007;         // 0x4208
    public const ushort CT_NEWPATCH_REQ = CT_PATCH + 0x000A;         // 0x420B (launcher) → CT_NEWPATCH_ACK
    public const ushort CT_NEWPATCH_ACK = CT_PATCH + 0x000B;         // 0x420C
    public const ushort CT_PREPATCHCOMPLETE_REQ = CT_PATCH + 0x000C; // 0x420D → TPreCompleteAdd, close session
    public const ushort CT_CHANGEIF_REQ = CT_PATCH + 0x000D;         // 0x420E → CT_NEWPATCH_ACK (interface files)

    // ---- CT_CONTROL plane ----
    public const ushort CT_SERVICEMONITOR_REQ = CT_CONTROL + 0x001D;   // 0x931E (reply to control)
    public const ushort CT_SERVICEMONITOR_ACK = CT_CONTROL + 0x001E;   // 0x931F (from control)
    public const ushort CT_SERVICEDATACLEAR_ACK = CT_CONTROL + 0x004F; // 0x9350 (from control, no-op)
    public const ushort CT_CTRLSVR_REQ = CT_CONTROL + 0x0058;          // 0x9359 (no-op)
}
