using AlexWoW.Database.Abstractions;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.World;

/// <summary>
/// Прогрессия (M9.1): кривая опыта (player_xp_for_level) + формула XP за убийство (CMaNGOS Formulas.h
/// <c>MaNGOS::XP</c>). Ленивая загрузка таблицы один раз. Формула BaseGain упрощена до content 1-60
/// (+45) — стартовые зоны; точный content по карте/зоне — позже.
/// </summary>
public sealed class LevelStore(IPlayerDataRepository worldDb, ILogger<LevelStore> logger)
{
    /// <summary>Максимальный уровень (WotLK).</summary>
    public const byte MaxLevel = 80;

    private volatile bool _loaded;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IReadOnlyDictionary<uint, uint> _xpForNext = new Dictionary<uint, uint>();

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
                _xpForNext = await worldDb.GetXpForLevelTableAsync(ct);
                logger.LogInformation("Кривая опыта (player_xp_for_level): загружено {Count} уровней", _xpForNext.Count);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "player_xp_for_level не загружен ({Msg}) — прокачка отключена", ex.Message);
                _xpForNext = new Dictionary<uint, uint>();
            }
            _loaded = true;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Опыт, нужный для перехода с <paramref name="level"/> на следующий (0 — если нет данных/кап).</summary>
    public uint XpToNext(byte level)
        => level >= MaxLevel ? 0u : _xpForNext.GetValueOrDefault(level, 0u);

    /// <summary>Доступна ли прокачка (таблица загружена).</summary>
    public bool Available => _xpForNext.Count > 0;

    // --- XP за убийство (CMaNGOS MaNGOS::XP) ---

    private static uint GetZeroDifference(uint level) => level switch
    {
        < 8 => 5,
        < 10 => 6,
        < 12 => 7,
        < 16 => 8,
        < 20 => 9,
        < 30 => 11,
        < 40 => 12,
        < 45 => 13,
        < 50 => 14,
        < 55 => 15,
        < 60 => 16,
        _ => 17,
    };

    private static bool IsTrivialLevelDifference(uint unitLvl, uint targetLvl)
    {
        if (unitLvl <= targetLvl)
            return false;
        var diff = unitLvl - targetLvl;
        return (unitLvl / 5) switch
        {
            0 or 1 => diff > 4,
            2 or 3 => diff > 5,
            4 or 5 => diff > 6,
            6 or 7 => diff > 7,
            _ => diff > 8,
        };
    }

    /// <summary>
    /// Базовый опыт за убийство существа (content 1-60). Возвращает 0 для серых (тривиальных) целей.
    /// Эталон: CMaNGOS <c>XP::BaseGain</c>.
    /// </summary>
    public uint KillXp(byte playerLevel, byte mobLevel)
    {
        var nBaseExp = playerLevel * 5 + 45; // content 1-60: +45
        if (mobLevel >= playerLevel)
        {
            var diff = Math.Min(4u, (uint)(mobLevel - playerLevel));
            return (uint)(nBaseExp * (1.0f + 0.05f * diff));
        }
        if (!IsTrivialLevelDifference(playerLevel, mobLevel))
        {
            var zd = GetZeroDifference(playerLevel);
            var diff = (uint)(playerLevel - mobLevel);
            return (uint)(nBaseExp * (1.0f - (float)diff / zd));
        }
        return 0; // серый — опыта нет
    }
}
