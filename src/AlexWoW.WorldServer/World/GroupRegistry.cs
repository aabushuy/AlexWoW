// Порт CMaNGOS-WoTLK: src/game/Globals/ObjectMgr.cpp (Group registry часть)
// https://github.com/cmangos/mangos-wotlk/blob/master/src/game/Globals/ObjectMgr.cpp. GPL-2.0.

namespace AlexWoW.WorldServer.World;

/// <summary>
/// Реестр групп в мире: id → Group + lookup charGuid → Group (быстрый доступ из опкод-handler'ов).
/// Эталон — sObjectMgr.AddGroup/GetGroupById/RemoveGroup в CMaNGOS.
/// </summary>
/// <remarks>
/// Хранится in-memory; персистентность БД — отдельный таск T6.
/// </remarks>
internal sealed class GroupRegistry
{
    private readonly Dictionary<uint, Group> _byId = [];
    private readonly Dictionary<ulong, Group> _byChar = [];   // включая invites — иначе invite-only Group теряется при concurrent invite на инициаторе
    private uint _nextId = 1;

    /// <summary>Создать новую invite-only Group с указанным лидером (приглашения добавляются отдельно).</summary>
    public Group CreateInviteOnly(ulong leaderGuid, string leaderName)
    {
        var g = new Group { Id = _nextId++, LeaderGuid = leaderGuid, LeaderName = leaderName };
        _byId[g.Id] = g;
        _byChar[leaderGuid] = g;
        return g;
    }

    /// <summary>Зарегистрировать invite — для lookup'а у получателя при accept'е.</summary>
    public void TrackInvite(Group group, ulong recipientGuid)
    {
        _byChar[recipientGuid] = group;
    }

    /// <summary>Пометить полноценным членом — invite snapshot снимается, char-lookup остаётся.</summary>
    public void OnMemberJoined(Group group, ulong charGuid)
    {
        _byChar[charGuid] = group;
    }

    /// <summary>Получить группу персонажа (вкл. invite-only состояние) или null.</summary>
    public Group? GetByChar(ulong charGuid) => _byChar.GetValueOrDefault(charGuid);

    /// <summary>Группа по id.</summary>
    public Group? GetById(uint groupId) => _byId.GetValueOrDefault(groupId);

    /// <summary>Стереть привязку персонажа (decline / leave / kick / disband).</summary>
    public void DetachChar(ulong charGuid)
    {
        _byChar.Remove(charGuid);
    }

    /// <summary>Распустить группу полностью — стирает все привязки и саму запись.</summary>
    public void Remove(Group group)
    {
        foreach (var m in group.Members)
            _byChar.Remove(m.Guid);
        foreach (var iv in group.Invites)
            _byChar.Remove(iv);
        _byChar.Remove(group.LeaderGuid);
        _byId.Remove(group.Id);
    }
}
