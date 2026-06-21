using TWorld.Protocol;

namespace TWorld.Server.World;

/// <summary>One entry of a country's castle-war top-3 ranking (C++ <c>TCASTLETOP3</c>).</summary>
public sealed class CastleTop3
{
    public uint Id { get; set; }
    public string Name { get; set; } = "";
    public ushort Point { get; set; }
}

/// <summary>
/// Per-castle war state aggregated from the map's occupation snapshot (C++ <c>TCASTLEWARINFO</c>).
/// <see cref="MapGuild"/> is each guild's total occupation bonus; <see cref="MapOccupy"/> is its per-day
/// (6-day) bonus history used for tie-breaking. After a recompute, <see cref="DefGuild"/>/<see cref="AtkGuild"/>
/// hold the selected defender/attacker and <see cref="Top3"/> the per-country leaderboards.
/// </summary>
public sealed class CastleWarInfo
{
    public ushort Id { get; set; }

    public Dictionary<uint, uint> MapGuild { get; } = new();        // guildId -> accumulated bonus
    public Dictionary<uint, ushort[]> MapOccupy { get; } = new();   // guildId -> 6-day bonus array (OCCUPY_ACCEPT only)
    public Dictionary<uint, uint> EnableGuild { get; } = new();     // working copy during selection

    /// <summary>Per-country (Defugel/Craxion) leaderboard, keyed by rank 1..3 (ascending) like the C++ std::map.</summary>
    public SortedDictionary<byte, CastleTop3>[] Top3 { get; } =
        Enumerable.Range(0, Proto.CountryCount).Select(_ => new SortedDictionary<byte, CastleTop3>()).ToArray();

    public uint AtkGuild { get; set; }
    public uint DefGuild { get; set; }
    public byte DefCountry { get; set; } = (byte)Contry.None;
    public ushort[] CountryPoint { get; } = new ushort[Proto.CountryCount];
}
