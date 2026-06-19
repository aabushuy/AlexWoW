// Порт CMaNGOS-WoTLK: src/game/Guilds/GuildMgr.cpp (LoadGuilds)
// https://github.com/cmangos/mangos-wotlk/blob/master/src/game/Guilds/GuildMgr.cpp. GPL-2.0.

using AlexWoW.Database.Abstractions;
using AlexWoW.WorldServer.World;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers.Guild;

/// <summary>При старте сервера загружает все гильдии в GuildRegistry. GUILD.T5.</summary>
internal sealed class GuildRecoveryHostedService(
    IGuildRepository repo,
    GuildRegistry registry,
    ILogger<GuildRecoveryHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            var rows = await repo.LoadAllAsync(ct);
            var loaded = 0;
            foreach (var (g, ranks, members) in rows)
            {
                if (members.Count == 0)
                {
                    await repo.DeleteGuildAsync(g.Id, ct);
                    continue;
                }
                var guild = registry.Rehydrate(g.Id, g.Name, g.LeaderGuid);
                guild.Motd = g.Motd;
                guild.InfoText = g.InfoText;
                guild.EmblemStyle = g.EmblemStyle;
                guild.EmblemColor = g.EmblemColor;
                guild.BorderStyle = g.BorderStyle;
                guild.BorderColor = g.BorderColor;
                guild.BackgroundColor = g.BackgroundColor;
                // Возвращаем ранки + членов через рефлексию internal-полей.
                foreach (var r in ranks)
                {
                    // Используем простой add через InitDefaultRanks() + перезапись имени/прав.
                    // Для простоты: переинициализируем дефолтами + патчим первые N.
                }
                guild.InitDefaultRanks();
                // Подменяем имена/права для первых N ранков (T5.1 — полная замена через расширение API Guild).
                for (var i = 0; i < ranks.Count && i < guild.Ranks.Count; i++)
                {
                    guild.Ranks[i].Name = ranks[i].Name;
                    guild.Ranks[i].Rights = (GuildRankRights)ranks[i].Rights;
                    guild.Ranks[i].BankMoneyPerDay = ranks[i].BankMoneyPerDay;
                }
                foreach (var m in members)
                {
                    guild.AddMember(new World.GuildMember
                    {
                        Guid = m.CharGuid,
                        Name = m.CharGuid.ToString("x"),  // stub — обновится на login
                        Class = 0,
                        Level = 0,
                        RankId = m.RankId,
                        PublicNote = m.PublicNote,
                        OfficerNote = m.OfficerNote,
                        JoinedAt = m.JoinedAt,
                        IsOnline = false,
                    });
                    registry.OnMemberJoined(guild, m.CharGuid);
                }
                loaded++;
            }
            logger.LogInformation("GUILD recovery: загружено {N} гильдий", loaded);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "GUILD recovery failed: {Msg}", ex.Message);
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
