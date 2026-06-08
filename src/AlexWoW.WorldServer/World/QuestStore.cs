using AlexWoW.Database;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.World;

/// <summary>
/// Реестр квест-связей (M6.5): множества entry существ, которые ДАЮТ (creature_questrelation) и
/// ПРИНИМАЮТ (creature_involvedrelation) квесты — для иконок «!»/«?». Ленивая загрузка один раз.
/// Полные тексты/цели/награды квестов придут в следующих инкрементах.
/// </summary>
public sealed class QuestStore(WorldDatabase worldDb, ILogger<QuestStore> logger)
{
    private static readonly uint[] None = [];
    private volatile bool _loaded;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IReadOnlyDictionary<uint, uint[]> _giverQuests = new Dictionary<uint, uint[]>();
    private IReadOnlyDictionary<uint, uint[]> _enderQuests = new Dictionary<uint, uint[]>();

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
                _giverQuests = ToMap(await worldDb.GetQuestGiverRelationsAsync(ct));
                _enderQuests = ToMap(await worldDb.GetQuestEnderRelationsAsync(ct));
                logger.LogInformation("Квест-связи: {Givers} дающих, {Enders} принимающих существ",
                    _giverQuests.Count, _enderQuests.Count);
            }
            catch (Exception ex)
            {
                logger.LogWarning("Квест-связи не загружены ({Msg}) — иконки квестов отключены", ex.Message);
                _giverQuests = new Dictionary<uint, uint[]>();
                _enderQuests = new Dictionary<uint, uint[]>();
            }
            _loaded = true;
        }
        finally
        {
            _lock.Release();
        }
    }

    private static Dictionary<uint, uint[]> ToMap(IEnumerable<Database.Models.QuestRelation> rows)
        => rows.GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.Select(r => r.Quest).Distinct().ToArray());

    /// <summary>Существо (по entry) даёт хотя бы один квест.</summary>
    public bool IsGiver(uint creatureEntry) => _giverQuests.ContainsKey(creatureEntry);

    /// <summary>Существо (по entry) принимает хотя бы один квест.</summary>
    public bool IsEnder(uint creatureEntry) => _enderQuests.ContainsKey(creatureEntry);

    /// <summary>Id квестов, которые ДАЁТ существо (для статуса «!»). M6.10.</summary>
    public uint[] GiverQuestIds(uint creatureEntry) => _giverQuests.GetValueOrDefault(creatureEntry, None);

    /// <summary>Id квестов, которые ПРИНИМАЕТ существо (для статуса «?»). M6.10.</summary>
    public uint[] EnderQuestIds(uint creatureEntry) => _enderQuests.GetValueOrDefault(creatureEntry, None);
}
