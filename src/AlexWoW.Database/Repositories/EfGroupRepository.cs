using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace AlexWoW.Database.Repositories;

/// <summary>
/// EF-репозиторий персистенции группы (GROUP.T6). Таблицы group_data + group_member, БД alexwow_auth.
/// Контекст из пула на каждую операцию (singleton-safe).
/// </summary>
public sealed class EfGroupRepository(IDbContextFactory<AuthDbContext> factory) : IGroupRepository
{
    public async Task<IReadOnlyList<(GroupData Group, IReadOnlyList<GroupMember> Members)>> LoadAllAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var groups = await db.Groups.AsNoTracking().ToListAsync(ct);
        if (groups.Count == 0)
            return [];
        var ids = groups.ConvertAll(g => g.Id);
        var members = await db.GroupMembers.AsNoTracking()
            .Where(m => ids.Contains(m.GroupId)).ToListAsync(ct);
        return [.. groups.Select(g => (g, (IReadOnlyList<GroupMember>)[..members.Where(m => m.GroupId == g.Id)]))];
    }

    public async Task<uint> InsertGroupAsync(GroupData group, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        db.Groups.Add(group);
        await db.SaveChangesAsync(ct);
        return group.Id;
    }

    public async Task UpdateGroupAsync(GroupData group, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        db.Groups.Update(group);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteGroupAsync(uint groupId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.GroupMembers.Where(m => m.GroupId == groupId).ExecuteDeleteAsync(ct);
        await db.Groups.Where(g => g.Id == groupId).ExecuteDeleteAsync(ct);
    }

    public async Task InsertMemberAsync(GroupMember member, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        db.GroupMembers.Add(member);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateMemberAsync(GroupMember member, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        db.GroupMembers.Update(member);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteMemberAsync(uint groupId, uint charGuid, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.GroupMembers
            .Where(m => m.GroupId == groupId && m.CharGuid == charGuid)
            .ExecuteDeleteAsync(ct);
    }
}
