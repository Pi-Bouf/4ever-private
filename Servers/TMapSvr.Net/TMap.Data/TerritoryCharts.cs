namespace TMap.Data;

/// <summary>
/// One war zone (C++ <c>TBATTLEZONE</c> from <c>TBATTLEZONECHART</c>): a fort (local), a castle, a mission area or the sky
/// garden — its map, parent castle, the spawns and switches its war uses, the reward items, the line (north / south), the two
/// zone buffs its own country gets inside it and the equipment level cap inside it.
/// </summary>
public sealed record BattleZone(ushort Id, string Name, ushort MapId, ushort Castle,
    ushort BossSpawnId, ushort LGateKeeperSpawnId, ushort RGateKeeperSpawnId, ushort CGateKeeperSpawnId,
    uint LSwitchId, uint RSwitchId, uint CSwitchId, ushort NormalItem, ushort ChiefItem, byte Line,
    ushort Skill1, ushort Skill2, byte ItemLevel,
    ushort ValorianBossDefend, ushort ValorianBossAttack, ushort DerionBossDefend, ushort DerionBossAttack,
    ushort MiddleSpawnId, ushort RightSpawnId, ushort LeftSpawnId);

/// <summary>LOCAL_TYPE (TMapType.h:157).</summary>
public static class LocalType
{
    public const byte Occupation = 1, Castle = 2, Mission = 3, SkyGarden = 4;
}

/// <summary>
/// The saved state of one territory (C++ <c>CTBLLocalTable</c> / <c>CTBLCastleTable</c> / <c>CTBLMissionTable</c> /
/// <c>CTBLSkygardenTable</c>): its owner country and guild (with the guild's name, empty when the guild is gone), when it was
/// taken, its next war (unix seconds), and its hero.
/// </summary>
public sealed record TerritoryRow(byte Type, ushort Id, byte Country, uint Guild, string GuildName, long Occupied, long NextDefend,
    string Hero, long HeroTime);

/// <summary>One day of a fort's week (C++ <c>CTBLLocalOccupy</c>): who held it that weekday (1 = Sunday) and how.</summary>
public sealed record LocalOccupyRow(ushort LocalId, byte Day, uint Guild, byte Type);
