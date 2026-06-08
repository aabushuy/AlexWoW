using AlexWoW.Database;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.World;

/// <summary>Базовые характеристики персонажа по уровню (M9.2): первичные статы + производные HP/мана.</summary>
public readonly record struct PlayerStats(
    uint Str, uint Agi, uint Sta, uint Int, uint Spi, uint MaxHealth, uint MaxMana);

/// <summary>
/// Характеристики по расе/классу/уровню (M9.2): из <c>player_levelstats</c> (str/agi/sta/int/spi) и
/// <c>player_classlevelstats</c> (базовые HP/мана). Производные: HP = basehp + бонус_от_стамины,
/// мана = basemana + бонус_от_интеллекта (формулы CMaNGOS). Ленивая загрузка один раз.
/// </summary>
public sealed class StatStore(WorldDatabase worldDb, ILogger<StatStore> logger)
{
    private volatile bool _loaded;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IReadOnlyDictionary<(byte, byte), (uint Hp, uint Mana)> _classLevel = new Dictionary<(byte, byte), (uint, uint)>();
    private IReadOnlyDictionary<(byte, byte, byte), (uint Str, uint Agi, uint Sta, uint Int, uint Spi)> _levelStats
        = new Dictionary<(byte, byte, byte), (uint, uint, uint, uint, uint)>();

    public async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_loaded)
            return;
        await _lock.WaitAsync(ct);
        try
        {
            if (_loaded)
                return;
            try
            {
                _classLevel = await worldDb.GetClassLevelStatsAsync(ct);
                _levelStats = await worldDb.GetLevelStatsAsync(ct);
                logger.LogInformation("Статы по уровню: {Cls} class/level, {Lvl} race/class/level",
                    _classLevel.Count, _levelStats.Count);
            }
            catch (Exception ex)
            {
                logger.LogWarning("player_levelstats не загружены ({Msg}) — статы по уровню флэтом", ex.Message);
            }
            _loaded = true;
        }
        finally
        {
            _lock.Release();
        }
    }

    public bool Available => _levelStats.Count > 0 && _classLevel.Count > 0;

    private static uint HealthFromStamina(uint sta) => sta <= 20 ? sta : 20 + (sta - 20) * 10;
    private static uint ManaFromIntellect(uint inte) => inte <= 20 ? inte : 20 + (inte - 20) * 15;

    /// <summary>Характеристики по расе/классу/уровню или null, если данных нет (фолбэк на флэт).</summary>
    public PlayerStats? Compute(byte race, byte cls, byte level)
    {
        if (!_levelStats.TryGetValue((race, cls, level), out var s)
            || !_classLevel.TryGetValue((cls, level), out var hm))
            return null;

        var maxHp = hm.Hp + HealthFromStamina(s.Sta);
        var maxMana = hm.Mana > 0 ? hm.Mana + ManaFromIntellect(s.Int) : 0u; // мана только у мана-классов
        return new PlayerStats(s.Str, s.Agi, s.Sta, s.Int, s.Spi, maxHp, maxMana);
    }
}
