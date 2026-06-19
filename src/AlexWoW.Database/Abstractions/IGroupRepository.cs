using AlexWoW.Database.Entities;

namespace AlexWoW.Database.Abstractions;

/// <summary>
/// Persistence группы (GROUP.T6). Сохраняем заголовок группы (group_data) и состав (group_member).
/// Восстанавливается при старте сервера или лениво при логине игрока.
/// </summary>
public interface IGroupRepository
{
    /// <summary>Загрузить все активные группы (для восстановления при старте сервера).</summary>
    Task<IReadOnlyList<(GroupData Group, IReadOnlyList<GroupMember> Members)>> LoadAllAsync(CancellationToken ct = default);

    /// <summary>Вставить заголовок группы; возвращает присвоенный id (auto-increment).</summary>
    Task<uint> InsertGroupAsync(GroupData group, CancellationToken ct = default);

    /// <summary>Обновить заголовок (leader/type/loot).</summary>
    Task UpdateGroupAsync(GroupData group, CancellationToken ct = default);

    /// <summary>Удалить группу + всех её членов (disband / auto-disband ≤1).</summary>
    Task DeleteGroupAsync(uint groupId, CancellationToken ct = default);

    /// <summary>Добавить нового члена.</summary>
    Task InsertMemberAsync(GroupMember member, CancellationToken ct = default);

    /// <summary>Обновить состояние члена (subgroup/assistant).</summary>
    Task UpdateMemberAsync(GroupMember member, CancellationToken ct = default);

    /// <summary>Удалить одного члена (kick/leave).</summary>
    Task DeleteMemberAsync(uint groupId, uint charGuid, CancellationToken ct = default);
}
