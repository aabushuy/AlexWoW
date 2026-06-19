// Порт CMaNGOS-WoTLK: src/game/Groups/GroupMgr.cpp (sObjectMgr.LoadGroups)
// https://github.com/cmangos/mangos-wotlk/blob/master/src/game/Groups/. GPL-2.0.

using AlexWoW.Database.Abstractions;
using AlexWoW.WorldServer.World;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers.Group;

/// <summary>
/// При старте сервера: загружает все активные группы из group_data + group_member и заполняет
/// <see cref="GroupRegistry"/>. После этого при логине игрока его группа уже в реестре,
/// и login-hook просто рассылает GROUP_LIST.
/// </summary>
internal sealed class GroupRecoveryHostedService(
    IGroupRepository repo,
    GroupRegistry registry,
    ILogger<GroupRecoveryHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            var rows = await repo.LoadAllAsync(ct);
            var loaded = 0;
            foreach (var (g, members) in rows)
            {
                if (members.Count == 0)
                {
                    // Группа без членов — сирота, удаляем.
                    await repo.DeleteGroupAsync(g.Id, ct);
                    continue;
                }
                var mem = registry.RehydrateGroup(g.Id, g.LeaderGuid, g.LeaderName,
                    (GroupType)g.Type, g.LootMethod, g.LootMasterGuid);
                foreach (var m in members)
                {
                    mem.AddMember(m.CharGuid, m.CharGuid.ToString("x")); // имя подтянется при первом login через UpdateMemberName
                    var member = mem.Members[^1];
                    member.IsAssistant = m.IsAssistant;
                    member.SubGroup = m.SubGroup;
                    member.IsOnline = false; // никто пока не в мире
                    registry.OnMemberJoined(mem, m.CharGuid);
                }
                loaded++;
            }
            logger.LogInformation("GROUP recovery: загружено {N} групп из БД", loaded);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "GROUP recovery failed: {Msg}", ex.Message);
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
