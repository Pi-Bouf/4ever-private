using TControl.Protocol;

namespace TControl.Server.Control;

/// <summary>
/// All live control-server state. Accessed only from the single batch task (the C++ used one batch
/// critical section), so plain dictionaries are safe — no concurrent access.
/// </summary>
public sealed class ControlState
{
    // Topology (loaded at startup).
    public Dictionary<byte, TMachine> Machines { get; } = new();
    public Dictionary<byte, TGroup> Groups { get; } = new();
    public Dictionary<byte, TSvrType> SvrTypes { get; } = new();
    public Dictionary<uint, ServiceInstance> Services { get; } = new(); // by MAKESVRID

    // Sessions.
    public HashSet<ManagerSession> Sessions { get; } = new();  // all connected manager sockets
    public List<ManagerSession> Managers { get; } = new();     // authenticated managers only

    // Scheduled events + chat bans + sellable cash items.
    public Dictionary<uint, EventInfo> Events { get; } = new();     // by index
    public Dictionary<uint, BanInfo> Bans { get; } = new();         // by ban seq
    public List<TCashItem> CashItems { get; } = new();

    public uint ManagerSeq { get; set; }
    public uint ChatBanSeq { get; set; }
    public uint EventIndex { get; set; }
    public bool AutoStart { get; set; }
    public string MyAddr { get; set; } = "";

    // Chat-ban fan-out/gather (mirrors the C++ m_dwSendCount / m_bChatBanSuccess pair; single-ban at a time).
    public uint ChatBanSendCount { get; set; }
    public bool ChatBanSuccess { get; set; }

    public ServiceInstance? FindService(uint id) => Services.TryGetValue(id, out var s) ? s : null;
    public ServiceInstance? FindService(byte group, byte type, byte id) => FindService(Proto.MakeSvrId(group, type, id));

    public ManagerSession? FindManager(uint id) => Managers.FirstOrDefault(m => m.Id == id);
    public ManagerSession? FindManagerByName(string strId) => Managers.FirstOrDefault(m => m.StrId == strId);

    public IEnumerable<ServiceInstance> Connected => Services.Values.Where(s => s.Conn is not null);
    public IEnumerable<ServiceInstance> ConnectedOfType(byte type) => Connected.Where(s => s.SvrType.Type == type);
}
