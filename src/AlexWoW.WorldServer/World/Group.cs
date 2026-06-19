// Порт CMaNGOS-WoTLK: src/game/Groups/Group.cpp + Group.h
// https://github.com/cmangos/mangos-wotlk/blob/master/src/game/Groups/Group.cpp. GPL-2.0.

namespace AlexWoW.WorldServer.World;

/// <summary>Тип группы: партия (≤5) или рейд (≤40, sub-groups в T5).</summary>
internal enum GroupType : byte
{
    Party = 0,
    Raid = 1,
}

/// <summary>Член группы: GUID, имя, sub-group (для рейда T5), статус ассистента и online-флаг (T2).</summary>
internal sealed class GroupMember
{
    public ulong Guid { get; init; }
    public required string Name { get; init; }
    public byte SubGroup { get; set; }      // T5: 0–7 для рейда; всегда 0 для партии
    public bool IsAssistant { get; set; }   // T5
    /// <summary>T2: онлайн ли член. Лидер при создании — true; при logout/login обновляется регистром.</summary>
    public bool IsOnline { get; set; } = true;
}

/// <summary>
/// Группа (партия/рейд). Эталон — CMaNGOS class Group (src/game/Groups/Group.h).
/// </summary>
/// <remarks>
/// Жизненный цикл: invite-only → Created (после accept'а первого приглашённого) → Disbanded.
/// T1 покрывает: создание invite-only, AddInvite/AcceptInvite, AddMember, RemoveInvite. Lim 5.
/// T2 — SMSG_GROUP_LIST broadcast. T3 — leader change/disband. T4 — XP/loot. T5 — raid. T6 — БД-перс.
/// </remarks>
internal sealed class Group
{
    public uint Id { get; init; }
    public ulong LeaderGuid { get; set; }
    public string LeaderName { get; set; } = "";
    public GroupType Type { get; set; } = GroupType.Party;
    public byte LootMethod { get; set; } // T4: 0=FFA, 1=RR, 2=Master, 3=Group
    public ulong LootMasterGuid { get; set; }
    public bool IsCreated { get; private set; } // true после первого AddMember (раньше — invite-only)

    private readonly List<GroupMember> _members = [];
    private readonly HashSet<ulong> _invites = [];

    public IReadOnlyList<GroupMember> Members => _members;
    public IReadOnlyCollection<ulong> Invites => _invites;
    public int MemberCount => _members.Count;

    /// <summary>Limit по типу. CMaNGOS MAX_GROUP_SIZE=5, MAX_RAID_SIZE=40.</summary>
    public int MaxSize => Type == GroupType.Raid ? 40 : 5;

    public bool IsFull => _members.Count >= MaxSize;
    public bool IsLeader(ulong guid) => LeaderGuid == guid;

    /// <summary>
    /// Счётчик SMSG_GROUP_LIST (CMaNGOS Group::m_counter, 3.3+). Растёт при каждой посылке —
    /// клиент использует для де-дупа. Расходуется через NextCounter().
    /// </summary>
    public uint NextCounter() => ++_counter;
    private uint _counter;

    public bool ContainsMember(ulong guid) => _members.Exists(m => m.Guid == guid);

    /// <summary>Добавить приглашение (до accept). Возвращает false, если уже есть/лимит.</summary>
    public bool AddInvite(ulong guid)
    {
        if (IsFull || _invites.Contains(guid) || ContainsMember(guid))
            return false;
        _invites.Add(guid);
        return true;
    }

    public bool HasInvite(ulong guid) => _invites.Contains(guid);
    public void RemoveInvite(ulong guid) => _invites.Remove(guid);
    public void ClearInvites() => _invites.Clear();

    /// <summary>
    /// Перевод приглашённого в члены группы. Если группа была invite-only — становится Created.
    /// Возвращает false, если лимит/не было приглашения.
    /// </summary>
    public bool AddMember(ulong guid, string name)
    {
        if (IsFull || ContainsMember(guid))
            return false;
        _members.Add(new GroupMember { Guid = guid, Name = name });
        _invites.Remove(guid);
        IsCreated = true;
        return true;
    }

    /// <summary>Удалить члена. Возвращает true, если был удалён.</summary>
    public bool RemoveMember(ulong guid)
    {
        var idx = _members.FindIndex(m => m.Guid == guid);
        if (idx < 0)
            return false;
        _members.RemoveAt(idx);
        return true;
    }
}
