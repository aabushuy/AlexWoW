using System.Collections.Concurrent;
using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Models;
using AlexWoW.WorldServer.Handlers;
using AlexWoW.WorldServer.Net;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.World;

/// <summary>
/// Рекордер захвата проверки заклинаний (M12 Spell QA): держит активную сессию захвата на <see cref="WorldSession"/>
/// и пишет результаты применённых эффектов в БД (<see cref="ISpellTestRepository"/>). Состояние — эфемерное,
/// admin-only, поэтому живёт в самом сервисе (а не в SessionState), keyed by session. Источник истины — серверный
/// расчёт: хук-точки в <see cref="SpellEffectsService"/> (прямой урон/хил) и <see cref="PeriodicsService"/> (тики).
/// Эталон (<paramref name="info"/>) сохраняется в строку в момент захвата — Web анализирует без доступа к mangos.
/// </summary>
internal sealed class SpellTestCaptureService(ISpellTestRepository repo, ILogger<SpellTestCaptureService> logger)
{
    /// <summary>Активный захват на сессию (ConcurrentDictionary: тики идут на потоке world-цикла, касты — на потоке сессии).</summary>
    private readonly ConcurrentDictionary<WorldSession, ActiveCapture> _active = new();

    internal sealed class ActiveCapture
    {
        public long SessionId;
        public byte Class;
        public byte Level;
        public SpellTestMode Mode;
        public int CastIndex;   // выставляется харнессом перед каждым кастом; 0 для ручного
        public int Recorded;    // счётчик записей (для .status)
    }

    internal bool IsActive(WorldSession s) => _active.ContainsKey(s);
    internal int RecordedCount(WorldSession s) => _active.TryGetValue(s, out var c) ? c.Recorded : 0;
    internal long? ActiveSessionId(WorldSession s) => _active.TryGetValue(s, out var c) ? c.SessionId : null;
    internal void SetCastIndex(WorldSession s, int idx) { if (_active.TryGetValue(s, out var c)) c.CastIndex = idx; }

    /// <summary>Стартует сессию захвата (заголовок в БД), возвращает false если нет персонажа или уже активна.</summary>
    internal async Task<bool> StartAsync(WorldSession session, SpellTestMode mode, string? note, CancellationToken ct)
    {
        if (session.Character is not { } ch || session.InWorldGuid == 0 || _active.ContainsKey(session))
            return false;
        // baseline-ожидание: таланты не расставлены (SpellMods пуст). Флаг фиксируем — Web покажет, если нарушено.
        var talents = session.Progression.SpellMods.Count > 0;
        var id = await repo.StartSessionAsync(ch.Guid, session.AccountId, ch.Class, ch.Level, mode, talents, note, ct);
        _active[session] = new ActiveCapture { SessionId = id, Class = ch.Class, Level = ch.Level, Mode = mode };
        logger.LogInformation("SpellTest старт '{User}' сессия={Id} режим={Mode}", session.Account, id, mode);
        return true;
    }

    /// <summary>Останавливает сессию (проставляет ended_at), возвращает false если не была активна.</summary>
    internal async Task<bool> StopAsync(WorldSession session, CancellationToken ct)
    {
        if (!_active.TryRemove(session, out var cap))
            return false;
        await repo.EndSessionAsync(cap.SessionId, ct);
        logger.LogInformation("SpellTest стоп '{User}' сессия={Id} записей={N}", session.Account, cap.SessionId, cap.Recorded);
        return true;
    }

    /// <summary>Запись прямого урона спеллом (хук <see cref="SpellEffectsService.ApplyDamageAsync"/>).</summary>
    internal Task RecordDamageAsync(WorldSession s, uint spellId, SpellCatalog.SpellInfo info,
        uint amount, uint overkill, CancellationToken ct)
        => RecordDirectAsync(s, spellId, info, SpellTestResultType.DirectDamage, amount, amount, overkill, ct);

    /// <summary>Запись прямого хила спеллом (хук <see cref="SpellEffectsService.ApplyHealAsync"/>).</summary>
    internal Task RecordHealAsync(WorldSession s, uint spellId, SpellCatalog.SpellInfo info,
        uint amount, uint effective, uint overheal, CancellationToken ct)
        => RecordDirectAsync(s, spellId, info, SpellTestResultType.DirectHeal, amount, effective, overheal, ct);

    private async Task RecordDirectAsync(WorldSession s, uint spellId, SpellCatalog.SpellInfo info,
        SpellTestResultType type, uint amount, uint effective, uint over, CancellationToken ct)
    {
        if (!_active.TryGetValue(s, out var cap))
            return; // пишем только при активной сессии
        var row = new SpellTestResult
        {
            SessionId = cap.SessionId,
            SpellId = spellId,
            Class = cap.Class,
            Level = cap.Level,
            ResultType = type,
            School = info.School,
            Amount = amount,
            Effective = effective,
            OverkillOrOverheal = over,
            ExpectedMin = (uint)Math.Max(0, info.MinAmount),
            ExpectedMax = (uint)Math.Max(0, info.MaxAmount),
            ExpectedCost = SpellCastService.EffectivePowerCost(s, info),
            PowerType = info.PowerType,
            IsHeal = info.IsHeal,
            WeaponBased = info.WeaponDamage || info.WeaponPercent > 0,
            FamilyName = info.FamilyName,
            CastIndex = (ushort)cap.CastIndex,
            RecordedAt = DateTime.UtcNow,
        };
        await SaveAsync(s, cap, row, ct);
    }

    /// <summary>
    /// Запись естественного тика DoT/HoT (хук <see cref="PeriodicsService"/>). В режиме харнесса пропускается —
    /// харнесс пишет синтетический тик (<see cref="RecordSyntheticTickAsync"/>), чтобы не дублировать записи.
    /// </summary>
    internal async Task RecordTickAsync(WorldSession s, uint spellId, byte school, bool isHeal,
        uint amount, uint expectedTick, CancellationToken ct)
    {
        if (!_active.TryGetValue(s, out var cap) || cap.Mode == SpellTestMode.Harness)
            return;
        var row = new SpellTestResult
        {
            SessionId = cap.SessionId,
            SpellId = spellId,
            Class = cap.Class,
            Level = cap.Level,
            ResultType = isHeal ? SpellTestResultType.HotTick : SpellTestResultType.DotTick,
            School = school,
            Amount = amount,
            Effective = amount,
            ExpectedMin = expectedTick,
            ExpectedMax = expectedTick,
            PowerType = 0,
            IsHeal = isHeal,
            CastIndex = (ushort)cap.CastIndex,
            RecordedAt = DateTime.UtcNow,
        };
        await SaveAsync(s, cap, row, ct);
    }

    /// <summary>
    /// Синтетический тик DoT/HoT для харнесса (M12 Spell QA, SQA-4): детерминированная запись эталонного тика
    /// (<c>info.TickAmount</c>) без ожидания world-цикла. Зовётся харнессом сразу после каста периодического спелла.
    /// </summary>
    internal async Task RecordSyntheticTickAsync(WorldSession s, uint spellId, SpellCatalog.SpellInfo info, CancellationToken ct)
    {
        if (!_active.TryGetValue(s, out var cap))
            return;
        var tick = (uint)Math.Max(0, info.TickAmount);
        var row = new SpellTestResult
        {
            SessionId = cap.SessionId,
            SpellId = spellId,
            Class = cap.Class,
            Level = cap.Level,
            ResultType = info.PeriodicHeal ? SpellTestResultType.HotTick : SpellTestResultType.DotTick,
            School = info.School,
            Amount = tick,
            Effective = tick,
            ExpectedMin = tick,
            ExpectedMax = tick,
            PowerType = info.PowerType,
            IsHeal = info.PeriodicHeal,
            FamilyName = info.FamilyName,
            CastIndex = (ushort)cap.CastIndex,
            RecordedAt = DateTime.UtcNow,
        };
        await SaveAsync(s, cap, row, ct);
    }

    private async Task SaveAsync(WorldSession s, ActiveCapture cap, SpellTestResult row, CancellationToken ct)
    {
        Interlocked.Increment(ref cap.Recorded);
        try
        {
            await repo.AddResultAsync(row, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SpellTest запись '{User}' spell={Spell}: {Msg}", s.Account, row.SpellId, ex.Message);
        }
    }
}
