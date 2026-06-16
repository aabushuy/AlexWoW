using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Models;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.World;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Обработчик очереди запросов на авто-прогон харнесса проверки заклинаний (M12 Spell QA, задача Vikunja 185 /
/// QA T1). Web/Claude вставляет pending-строку в <c>spell_test_request</c>, этот сервис (вызывается из
/// <see cref="WorldTick"/> раз в такт) атомарно забирает её (pending→running), находит онлайн-сессию аккаунта и
/// запускает <see cref="SpellTestHarnessService.RunAsync"/>. По завершении пишет результат в строку: done + id
/// созданной сессии захвата (Claude читает его SELECT'ом) либо failed + причина.
/// <para>Hands-off режим: заменяет чат-команду <c>.spelltest run</c> — Claude инициирует прогон SQL-INSERT'ом,
/// без клиента и без ввода в чате. Interim-решение: всё ещё требует залогиненного в мире персонажа (до T2).</para>
/// </summary>
internal sealed class SpellTestRequestService(
    ISpellTestRepository repo,
    WorldState world,
    SpellTestHarnessService harness,
    ILogger<SpellTestRequestService> logger)
{
    /// <summary>Гард параллельных прогонов: прогон занимает десятки-сотни мс (много кастов), и одновременные
    /// прогоны нескольких персонажей конфликтуют по манекенам/позиции. Один in-flight за раз — по мере готовности
    /// берём следующий из очереди. Не в сессии (SRP): это свойство самого обработчика очереди.</summary>
    private int _inFlight;

    /// <summary>Зовётся из WorldTick: если нет активного прогона — забирает ОДНУ pending-строку и запускает её
    /// (fire-and-forget). Не блокирует тик мира (прогон идёт в фоновой задаче, исключения логируются внутри).</summary>
    internal async Task TickAsync(CancellationToken ct)
    {
        if (Interlocked.CompareExchange(ref _inFlight, 1, 0) != 0)
            return; // уже идёт прогон — пропускаем такт

        SpellTestRequestClaim? claim;
        try
        {
            claim = await repo.ClaimPendingRequestAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SpellTest-queue: не прочитать очередь: {Msg}", ex.Message);
            Volatile.Write(ref _inFlight, 0);
            return;
        }
        if (claim is null)
        {
            Volatile.Write(ref _inFlight, 0); // пусто — освободили гард, ждём следующий такт
            return;
        }

        // Прогон в фоновой задаче: WorldTick не ждёт его (мир продолжает тикать). Session живёт дольше задачи
        // (это онлайн-игрок), _inFlight снимется по завершении. Исключения — внутри try.
        _ = Task.Run(() => ProcessAsync(claim, CancellationToken.None));
    }

    private async Task ProcessAsync(SpellTestRequestClaim claim, CancellationToken ct)
    {
        try
        {
            // Target-сессия: онлайн-игрок с именем аккаунта из запроса (case-insensitive — логины ASCII-устойчивы,
            // но приведение безопасно от опечаток регистра). Персонаж должен быть В МИРЕ (InWorldGuid != 0).
            var target = world.Players.FirstOrDefault(p =>
                p.Session.InWorldGuid != 0
                && string.Equals(p.Session.Account, claim.Account, StringComparison.OrdinalIgnoreCase));

            if (target is null)
            {
                await repo.CompleteRequestAsync(claim.Id, success: false, sessionId: null,
                    error: $"нет онлайн-сессии аккаунта '{claim.Account}' (или персонаж не в мире)", ct);
                logger.LogInformation("SpellTest-queue '{Account}': нет онлайн-сессии (запрос #{Id})", claim.Account, claim.Id);
                return;
            }

            // casts: 0 из БД → дефолт 5 (как в .spelltest run). Харкесс клампит в 1..50.
            var casts = claim.Casts > 0 ? claim.Casts : 5;
            var run = await harness.RunAsync(target.Session, casts, ct);
            if (run.Tested < 0)
            {
                await repo.CompleteRequestAsync(claim.Id, success: false, sessionId: null,
                    error: "персонаж не в мире на момент прогона", ct);
                return;
            }
            await repo.CompleteRequestAsync(claim.Id, success: true, sessionId: run.SessionId, error: null, ct);
            logger.LogInformation("SpellTest-queue '{Account}': прогон #{Id} готов — спеллов {Tested}, сессия {Sid}",
                claim.Account, claim.Id, run.Tested, run.SessionId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SpellTest-queue '{Account}' (запрос #{Id}): {Msg}", claim.Account, claim.Id, ex.Message);
            try
            {
                await repo.CompleteRequestAsync(claim.Id, success: false, sessionId: null,
                    error: $"исключение: {ex.Message}", ct);
            }
            catch { /* финализация не критична — строка останется running, видна в логе/БД при разборе */ }
        }
        finally
        {
            Volatile.Write(ref _inFlight, 0);
        }
    }
}
