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
    private volatile bool _loaded;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private HashSet<uint> _givers = new();
    private HashSet<uint> _enders = new();

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
                _givers = new HashSet<uint>(await worldDb.GetQuestGiverEntriesAsync(ct));
                _enders = new HashSet<uint>(await worldDb.GetQuestEnderEntriesAsync(ct));
                logger.LogInformation("Квест-связи: {Givers} дающих, {Enders} принимающих существ", _givers.Count, _enders.Count);
            }
            catch (Exception ex)
            {
                logger.LogWarning("Квест-связи не загружены ({Msg}) — иконки квестов отключены", ex.Message);
                _givers = new HashSet<uint>();
                _enders = new HashSet<uint>();
            }
            _loaded = true;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Существо (по entry) даёт хотя бы один квест.</summary>
    public bool IsGiver(uint creatureEntry) => _givers.Contains(creatureEntry);

    /// <summary>Существо (по entry) принимает хотя бы один квест.</summary>
    public bool IsEnder(uint creatureEntry) => _enders.Contains(creatureEntry);
}
