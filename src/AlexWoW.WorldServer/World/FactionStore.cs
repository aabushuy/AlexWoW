using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Models;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.World;

/// <summary>
/// Реестр реакций фракций (M6.7 авто-агро): загружает faction_template (из FactionTemplate.dbc) и
/// считает враждебность по правилам CMaNGOS. Загрузка ленивая (один раз, потокобезопасно). Если таблицы
/// нет/пуста — <see cref="IsHostile"/> всегда false (авто-агро просто не работает, остальное — как есть).
/// </summary>
public sealed class FactionStore(IWorldRepository worldDb, ILogger<FactionStore> logger)
{
    private volatile bool _loaded;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IReadOnlyDictionary<uint, FactionTemplateRow> _byId = new Dictionary<uint, FactionTemplateRow>();

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
                var rows = await worldDb.GetFactionTemplatesAsync(ct);
                _byId = rows.ToDictionary(r => r.Id);
                logger.LogInformation("Реакции фракций (faction_template): загружено {Count}", _byId.Count);
            }
            catch (Exception ex)
            {
                logger.LogWarning("faction_template не загружен ({Msg}) — авто-агро отключено "
                    + "(прогоните tools/MapExtractor factiontemplate и залейте SQL)", ex.Message);
                _byId = new Dictionary<uint, FactionTemplateRow>();
            }
            _loaded = true;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Враждебна ли фракция-шаблон <paramref name="aId"/> (существо) к <paramref name="bId"/> (игрок).
    /// Логика CMaNGOS: явный враг по enemy[] → да; явный друг по friend[] → нет; иначе hostileMask &amp; ourMask.
    /// </summary>
    public bool IsHostile(uint aId, uint bId)
    {
        if (!_byId.TryGetValue(aId, out var a) || !_byId.TryGetValue(bId, out var b))
            return false; // нет данных — безопасно считаем не враждебным

        if (b.Faction != 0)
        {
            if (a.Enemy1 == b.Faction || a.Enemy2 == b.Faction || a.Enemy3 == b.Faction || a.Enemy4 == b.Faction)
                return true;
            if (a.Friend1 == b.Faction || a.Friend2 == b.Faction || a.Friend3 == b.Faction || a.Friend4 == b.Faction)
                return false;
        }
        return (a.HostileMask & b.OurMask) != 0;
    }
}
