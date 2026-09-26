using TLogin.Protocol;

namespace TBot;

/// <summary>
/// One logged-in bot for a scripted scenario: logs in, enters the map, then a background thread files every
/// packet the server pushes into an inbox the scenario waits on. Unlike <see cref="BotRunner"/>'s walk, nothing
/// is dropped — a reply that lands before the scenario asks for it is still there when it does.
/// </summary>
public sealed class Bot : IDisposable
{
    private readonly BotConfig _cfg;
    private readonly List<byte[]> _inbox = new();
    private readonly object _gate = new();
    private BotConnection? _conn;
    private Thread? _reader;
    private volatile bool _stopping;

    public string Tag => _cfg.LogTag;
    public uint CharId { get; private set; }
    public CharSpawn Spawn { get; private set; } = null!;
    public bool Closed { get; private set; }
    public int BadPackets => _conn?.BadPackets ?? 0;

    /// <summary>Every packet received, in order, for the byte-level report.</summary>
    public List<byte[]> Received { get; } = new();

    /// <summary>The raw bytes behind each reader handed out, for failure reports.</summary>
    public static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PacketReader, byte[]> Raw = new();

    public Bot(BotConfig cfg) => _cfg = cfg;

    public void Enter()
    {
        var runner = new BotRunner(_cfg);
        var login = runner.RunLogin();
        CharId = login.CharId;
        _conn = new BotConnection(login.WorldHost, login.WorldPort, !_cfg.MapNoCrypt);
        var early = new List<byte[]>();
        Spawn = runner.ConnectAndEnter(_conn, login, early);
        lock (_gate) { _inbox.AddRange(early); Received.AddRange(early); }
        _reader = new Thread(ReadLoop) { IsBackground = true, Name = $"bot-{Tag}" };
        _reader.Start();
    }

    private void ReadLoop()
    {
        try
        {
            while (!_stopping)
            {
                if (_conn!.TryReceive(TimeSpan.FromMilliseconds(200)) is null) continue;
                var pkt = _conn.LastPacket!;
                lock (_gate)
                {
                    _inbox.Add(pkt);
                    Received.Add(pkt);
                    Monitor.PulseAll(_gate);
                }
            }
        }
        catch (Exception) when (!_stopping)
        {
            lock (_gate) { Closed = true; Monitor.PulseAll(_gate); }
        }
        catch (Exception) { }
    }

    public void Send(PacketWriter w) => _conn!.Send(w);

    /// <summary>Takes the first packet with this id (and matching <paramref name="match"/>) from the inbox,
    /// waiting up to <paramref name="timeoutMs"/>; null on timeout.</summary>
    public PacketReader? TryWait(ushort id, Func<PacketReader, bool>? match = null, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        lock (_gate)
        {
            while (true)
            {
                for (int i = 0; i < _inbox.Count; i++)
                {
                    if (PacketHeader.ReadId(_inbox[i]) != id) continue;
                    if (match is not null && !match(new PacketReader(_inbox[i]))) continue;
                    var pkt = _inbox[i];
                    _inbox.RemoveAt(i);
                    var reader = new PacketReader(pkt);
                    Raw.AddOrUpdate(reader, pkt);
                    return reader;
                }
                var left = deadline - DateTime.UtcNow;
                if (left <= TimeSpan.Zero || Closed) return null;
                Monitor.Wait(_gate, left);
            }
        }
    }

    /// <summary>Drops every queued packet with this id (e.g. stale replies before a fresh request).</summary>
    public void Discard(ushort id)
    {
        lock (_gate) _inbox.RemoveAll(p => PacketHeader.ReadId(p) == id);
    }

    public void Dispose()
    {
        _stopping = true;
        _conn?.Dispose();
        _reader?.Join(1000);
    }
}
