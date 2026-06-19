using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Models;
using Microsoft.EntityFrameworkCore;
using EntityRequest = AlexWoW.Database.Entities.SpellTestRequest;
using EntityResult = AlexWoW.Database.Entities.SpellTestResult;
using EntitySession = AlexWoW.Database.Entities.SpellTestSession;
using ModelResult = AlexWoW.Database.Models.SpellTestResult;
using ModelSession = AlexWoW.Database.Models.SpellTestSession;

namespace AlexWoW.Database.Repositories;

/// <summary>
/// EF-репозиторий захвата проверки заклинаний (таблицы spell_test_*, БД alexwow_auth). M12 Spell QA.
/// Контекст из пула на операцию (singleton-safe). Запись — со стороны world-сервера, чтение — со стороны Web.
/// </summary>
public sealed class EfSpellTestRepository(IDbContextFactory<AuthDbContext> factory) : ISpellTestRepository
{
    public async Task<long> StartSessionAsync(uint ownerGuid, uint accountId, byte @class, byte level,
        SpellTestMode mode, bool talentsSlotted, string? note, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = new EntitySession
        {
            OwnerGuid = ownerGuid,
            AccountId = accountId,
            Class = @class,
            Level = level,
            Mode = (byte)mode,
            TalentsSlotted = (byte)(talentsSlotted ? 1 : 0),
            StartedAt = DateTime.UtcNow,
            Note = note,
        };
        db.SpellTestSessions.Add(row);
        await db.SaveChangesAsync(ct);
        return row.Id;
    }

    public async Task EndSessionAsync(long sessionId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.SpellTestSessions.Where(x => x.Id == sessionId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.EndedAt, DateTime.UtcNow), ct);
    }

    public async Task AddResultAsync(ModelResult row, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        db.SpellTestResults.Add(ToEntity(row));
        await db.SaveChangesAsync(ct);
    }

    public async Task AddResultsAsync(IReadOnlyList<ModelResult> rows, CancellationToken ct = default)
    {
        if (rows.Count == 0)
            return;
        await using var db = await factory.CreateDbContextAsync(ct);
        db.SpellTestResults.AddRange(rows.Select(ToEntity));
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ModelSession>> GetSessionsAsync(int limit = 100, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.SpellTestSessions.AsNoTracking()
            .OrderByDescending(x => x.Id).Take(limit).ToListAsync(ct);
        return [.. rows.Select(MapSession)];
    }

    public async Task<ModelSession?> GetSessionAsync(long sessionId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.SpellTestSessions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == sessionId, ct);
        return row is null ? null : MapSession(row);
    }

    public async Task<IReadOnlyList<ModelResult>> GetResultsAsync(long sessionId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.SpellTestResults.AsNoTracking()
            .Where(x => x.SessionId == sessionId)
            .OrderBy(x => x.SpellId).ThenBy(x => x.CastIndex).ThenBy(x => x.Id).ToListAsync(ct);
        return [.. rows.Select(MapResult)];
    }

    public async Task MarkAnalyzedAsync(long sessionId, uint ticketId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.SpellTestSessions.Where(x => x.Id == sessionId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Analyzed, (byte)1)
                .SetProperty(x => x.TicketId, ticketId), ct);
    }

    // --- QA T1: очередь запросов на авто-прогон харнесса (DB-flag + World-tick) ---

    public async Task<long> EnqueueRequestAsync(string account, int casts, string? note, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = new EntityRequest
        {
            Account = account,
            Casts = casts,
            Note = note,
            Status = (byte)SpellTestRequestStatus.Pending,
            RequestedAt = DateTime.UtcNow,
        };
        db.SpellTestRequests.Add(row);
        await db.SaveChangesAsync(ct);
        return row.Id;
    }

    public async Task<SpellTestRequestClaim?> ClaimPendingRequestAsync(CancellationToken ct = default)
    {
        // FIFO: старейшая pending-строка (по requested_at). Атомарный CAS pending→running через ExecuteUpdate
        // с фильтром по status=Pending — защищает от двойной выборки, если World-tick идёт параллельно.
        await using var db = await factory.CreateDbContextAsync(ct);
        var oldest = await db.SpellTestRequests
            .Where(x => x.Status == (byte)SpellTestRequestStatus.Pending)
            .OrderBy(x => x.RequestedAt)
            .FirstOrDefaultAsync(ct);
        if (oldest is null)
            return null;
        var claimed = await db.SpellTestRequests
            .Where(x => x.Id == oldest.Id && x.Status == (byte)SpellTestRequestStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, (byte)SpellTestRequestStatus.Running), ct);
        if (claimed == 0)
            return null; // кто-то успел перехватить между SELECT и CAS — пропускаем такт
        return new SpellTestRequestClaim
        {
            Id = oldest.Id,
            Account = oldest.Account,
            Casts = oldest.Casts,
            Note = oldest.Note,
        };
    }

    public async Task CompleteRequestAsync(long requestId, bool success, long? sessionId, string? error, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.SpellTestRequests.Where(x => x.Id == requestId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, success
                    ? (byte)SpellTestRequestStatus.Done
                    : (byte)SpellTestRequestStatus.Failed)
                .SetProperty(x => x.SessionId, sessionId)
                .SetProperty(x => x.Error, error)
                .SetProperty(x => x.ProcessedAt, DateTime.UtcNow), ct);
    }

    public async Task<SpellTestRequestView?> GetRequestAsync(long requestId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var x = await db.SpellTestRequests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == requestId, ct);
        return x is null ? null : new SpellTestRequestView
        {
            Id = x.Id,
            Account = x.Account,
            Casts = x.Casts,
            Note = x.Note,
            Status = (SpellTestRequestStatus)x.Status,
            SessionId = x.SessionId,
            Error = x.Error,
            RequestedAt = x.RequestedAt,
            ProcessedAt = x.ProcessedAt,
        };
    }

    private static EntityResult ToEntity(ModelResult x) => new()
    {
        SessionId = x.SessionId,
        SpellId = x.SpellId,
        Class = x.Class,
        Level = x.Level,
        ResultType = (byte)x.ResultType,
        School = x.School,
        Amount = x.Amount,
        Effective = x.Effective,
        OverkillOrOverheal = x.OverkillOrOverheal,
        ExpectedMin = x.ExpectedMin,
        ExpectedMax = x.ExpectedMax,
        ExpectedCost = x.ExpectedCost,
        PowerType = x.PowerType,
        IsHeal = (byte)(x.IsHeal ? 1 : 0),
        WeaponBased = (byte)(x.WeaponBased ? 1 : 0),
        FamilyName = x.FamilyName,
        CastIndex = x.CastIndex,
        RecordedAt = x.RecordedAt == default ? DateTime.UtcNow : x.RecordedAt,
    };

    private static ModelSession MapSession(EntitySession x) => new()
    {
        Id = x.Id,
        OwnerGuid = x.OwnerGuid,
        AccountId = x.AccountId,
        Class = x.Class,
        Level = x.Level,
        Mode = (SpellTestMode)x.Mode,
        TalentsSlotted = x.TalentsSlotted != 0,
        StartedAt = x.StartedAt,
        EndedAt = x.EndedAt,
        Note = x.Note,
        Analyzed = x.Analyzed != 0,
        TicketId = x.TicketId,
    };

    private static ModelResult MapResult(EntityResult x) => new()
    {
        Id = x.Id,
        SessionId = x.SessionId,
        SpellId = x.SpellId,
        Class = x.Class,
        Level = x.Level,
        ResultType = (SpellTestResultType)x.ResultType,
        School = x.School,
        Amount = x.Amount,
        Effective = x.Effective,
        OverkillOrOverheal = x.OverkillOrOverheal,
        ExpectedMin = x.ExpectedMin,
        ExpectedMax = x.ExpectedMax,
        ExpectedCost = x.ExpectedCost,
        PowerType = x.PowerType,
        IsHeal = x.IsHeal != 0,
        WeaponBased = x.WeaponBased != 0,
        FamilyName = x.FamilyName,
        CastIndex = x.CastIndex,
        RecordedAt = x.RecordedAt,
    };
}
