namespace AlexWoW.Database.Entities;

/// <summary>
/// EF-сущность таблицы <c>group_data</c> (БД alexwow_auth) — persistence группы (GROUP.T6).
/// Один ряд = одна активная группа. При disband — DELETE; при изменении лидера/типа/лут-настроек — UPDATE.
/// </summary>
public sealed class GroupData
{
    /// <summary>PK (auto-increment) — соответствует Group.Id в памяти.</summary>
    public uint Id { get; set; }

    /// <summary>GUID лидера (низшие 32 бита player.Guid; HIGHGUID_PLAYER маска не сохраняется).</summary>
    public uint LeaderGuid { get; set; }

    /// <summary>Имя лидера — для быстрой отдачи SMSG_GROUP_LIST без подгрузки персонажа.</summary>
    public string LeaderName { get; set; } = "";

    /// <summary>0 — party, 1 — raid (см. GroupType в WorldServer).</summary>
    public byte Type { get; set; }

    /// <summary>0..3 — FFA/RR/Master/Group (GROUP.T4).</summary>
    public byte LootMethod { get; set; }

    /// <summary>GUID мастер-лутера (только если LootMethod=Master). 0 если не выставлено.</summary>
    public uint LootMasterGuid { get; set; }
}

/// <summary>
/// EF-сущность таблицы <c>group_member</c> — состав группы. Composite PK (GroupId, CharGuid).
/// Удаляется вместе с GroupData (cascade) или при kick/leave.
/// </summary>
public sealed class GroupMember
{
    public uint GroupId { get; set; }
    public uint CharGuid { get; set; }
    public byte SubGroup { get; set; }     // 0..7 (рейд T5)
    public bool IsAssistant { get; set; }
}
