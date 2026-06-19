// Порт CMaNGOS-WoTLK: src/game/Groups/Group.cpp (Save*/_addMember/_removeMember persistence части)
// https://github.com/cmangos/mangos-wotlk/blob/master/src/game/Groups/Group.cpp. GPL-2.0.

using AlexWoW.Database.Abstractions;
using AlexWoW.WorldServer.World;
using Microsoft.Extensions.Logging;
using EfGroupData = AlexWoW.Database.Entities.GroupData;
using EfGroupMember = AlexWoW.Database.Entities.GroupMember;

namespace AlexWoW.WorldServer.Handlers.Group;

/// <summary>
/// Persistence группы (GROUP.T6): мост между in-memory <see cref="World.Group"/> и БД <c>group_data</c>/<c>group_member</c>.
/// Handlers вызывают синхронные in-memory изменения + fire-and-forget или await вызов
/// соответствующих SaveXxxAsync. Ошибки БД логируются и НЕ рушат gameplay (как logout-сейв характеристик).
/// </summary>
internal sealed class GroupPersistenceService(IGroupRepository repo, ILogger<GroupPersistenceService> logger)
{
    /// <summary>Создание группы в БД при первом accept'е (invite-only → Created).</summary>
    public async Task SaveNewGroupAsync(World.Group group, CancellationToken ct)
    {
        try
        {
            var ef = new EfGroupData
            {
                LeaderGuid = (uint)group.LeaderGuid,
                LeaderName = group.LeaderName,
                Type = (byte)group.Type,
                LootMethod = group.LootMethod,
                LootMasterGuid = (uint)group.LootMasterGuid,
            };
            var newId = await repo.InsertGroupAsync(ef, ct);
            group.PersistedId = newId;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "GROUP persistence: SaveNewGroup id={Id} failed: {Msg}", group.Id, ex.Message);
        }
    }

    /// <summary>Обновление заголовка (leader/type/loot).</summary>
    public async Task UpdateGroupAsync(World.Group group, CancellationToken ct)
    {
        if (group.PersistedId == 0)
            return;
        try
        {
            await repo.UpdateGroupAsync(new EfGroupData
            {
                Id = group.PersistedId,
                LeaderGuid = (uint)group.LeaderGuid,
                LeaderName = group.LeaderName,
                Type = (byte)group.Type,
                LootMethod = group.LootMethod,
                LootMasterGuid = (uint)group.LootMasterGuid,
            }, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "GROUP persistence: UpdateGroup id={Id} failed: {Msg}", group.PersistedId, ex.Message);
        }
    }

    public async Task DeleteGroupAsync(World.Group group, CancellationToken ct)
    {
        if (group.PersistedId == 0)
            return;
        try { await repo.DeleteGroupAsync(group.PersistedId, ct); }
        catch (Exception ex) { logger.LogWarning(ex, "GROUP persistence: DeleteGroup id={Id} failed", group.PersistedId); }
    }

    public async Task SaveMemberAsync(World.Group group, World.GroupMember member, CancellationToken ct)
    {
        if (group.PersistedId == 0)
            return;
        try
        {
            await repo.InsertMemberAsync(new EfGroupMember
            {
                GroupId = group.PersistedId,
                CharGuid = (uint)member.Guid,
                SubGroup = member.SubGroup,
                IsAssistant = member.IsAssistant,
            }, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "GROUP persistence: SaveMember group={GroupId} char={Char} failed: {Msg}",
                group.PersistedId, member.Guid, ex.Message);
        }
    }

    public async Task UpdateMemberAsync(World.Group group, World.GroupMember member, CancellationToken ct)
    {
        if (group.PersistedId == 0)
            return;
        try
        {
            await repo.UpdateMemberAsync(new EfGroupMember
            {
                GroupId = group.PersistedId,
                CharGuid = (uint)member.Guid,
                SubGroup = member.SubGroup,
                IsAssistant = member.IsAssistant,
            }, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "GROUP persistence: UpdateMember group={GroupId} char={Char} failed",
                group.PersistedId, member.Guid);
        }
    }

    public async Task DeleteMemberAsync(World.Group group, ulong charGuid, CancellationToken ct)
    {
        if (group.PersistedId == 0)
            return;
        try { await repo.DeleteMemberAsync(group.PersistedId, (uint)charGuid, ct); }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "GROUP persistence: DeleteMember group={GroupId} char={Char} failed",
                group.PersistedId, charGuid);
        }
    }
}
