using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Entities;
using Microsoft.EntityFrameworkCore;
using ModelQuestStatusRow = AlexWoW.Database.Models.QuestStatusRow;

namespace AlexWoW.Database.Repositories;

/// <summary>
/// EF-репозиторий статусов квестов персонажа (таблица character_queststatus, БД alexwow_auth).
/// SRP-часть DAL (#24). Контекст из пула на операцию.
/// </summary>
public sealed class EfQuestRepository(IDbContextFactory<AuthDbContext> factory) : IQuestRepository
{
    public async Task<IReadOnlyList<ModelQuestStatusRow>> GetQuestStatusesAsync(uint ownerGuid, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.QuestStatuses.AsNoTracking().Where(x => x.OwnerGuid == ownerGuid).ToListAsync(ct);
        return [.. rows.Select(x => new ModelQuestStatusRow
        {
            QuestId = x.QuestId,
            Slot = x.Slot,
            Status = x.Status,
            Counter0 = x.Counter0,
            Counter1 = x.Counter1,
            Counter2 = x.Counter2,
            Counter3 = x.Counter3,
        })];
    }

    public async Task UpsertQuestStatusAsync(uint ownerGuid, uint questId, byte slot, byte status,
        ushort c0, ushort c1, ushort c2, ushort c3, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var e = await db.QuestStatuses.FindAsync([ownerGuid, questId], ct);
        if (e is null)
        {
            e = new CharacterQuestStatus { OwnerGuid = ownerGuid, QuestId = questId };
            db.QuestStatuses.Add(e);
        }
        e.Slot = slot;
        e.Status = status;
        e.Counter0 = c0;
        e.Counter1 = c1;
        e.Counter2 = c2;
        e.Counter3 = c3;
        await db.SaveChangesAsync(ct);
    }
}
