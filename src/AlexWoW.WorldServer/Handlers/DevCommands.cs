using System.Text;
using AlexWoW.Common.Network;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Дев/тест-команды через чат (M9.4): сообщение, начинающееся с '.', не уходит в чат, а выполняет
/// команду (прокачка/выдача предметов для теста). Гейт — env <c>WORLD_DEV_COMMANDS</c> (по умолчанию ВКЛ;
/// "0" — выкл). ⚠️ Для прод-сервера гейтить по gmlevel аккаунта (сейчас — тестовый сервер).
/// Команды: <c>.level N</c>, <c>.xp [add] N</c>, <c>.additem ID [count]</c>, <c>.learn SPELL</c>,
/// <c>.learnall</c> (все доступные по уровню абилки у ближайшего тренера), <c>.buff/.unbuff SPELL</c>,
/// <c>.dummy</c>, <c>.help</c>.
/// </summary>
public static class DevCommands
{
    /// <summary>Выполнить, если это дев-команда. true → обработано (в чат не слать).
    /// Доступ — только аккаунтам с флагом администратора (account.is_admin = 1). M7.</summary>
    public static async Task<bool> TryHandleAsync(WorldSession session, string text, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(text) || text[0] != '.')
            return false;
        if (!session.IsAdmin)
        {
            await ReplyAsync(session, "Команда доступна только администраторам.", ct);
            return true; // в чат не уходит (это была попытка команды)
        }

        var parts = text[1..].Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return false;
        var cmd = parts[0].ToLowerInvariant();

        try
        {
            switch (cmd)
            {
                case "level" or "lvl" when parts.Length >= 2 && byte.TryParse(parts[1], out var lvl):
                    await Progression.SetLevelAsync(session, lvl, ct);
                    await ReplyAsync(session, $"Уровень: {Math.Clamp(lvl, (byte)1, World.LevelStore.MaxLevel)}", ct);
                    return true;

                case "xp" when TryParseXp(parts, out var amount):
                    await Progression.GiveXpAsync(session, amount, ct);
                    await ReplyAsync(session, $"Опыт +{amount}", ct);
                    return true;

                case "additem" or "item" when parts.Length >= 2 && uint.TryParse(parts[1], out var itemId):
                    var qty = parts.Length >= 3 && uint.TryParse(parts[2], out var q) ? q : 1u;
                    var item = await InventoryGrant.TryGiveAsync(session, itemId, qty, ct);
                    await ReplyAsync(session, item is null ? "Нет места в сумке" : $"Выдан предмет {itemId} x{qty}", ct);
                    return true;

                case "learn" when parts.Length >= 2 && uint.TryParse(parts[1], out var spellId):
                    await LearnSpellAsync(session, spellId, ct);
                    await ReplyAsync(session, $"Изучен спелл {spellId}", ct);
                    return true;

                case "learnall":
                    if (session.InWorldGuid == 0)
                        await ReplyAsync(session, "Доступно только в мире", ct);
                    else
                    {
                        var n = await TrainerHandlers.LearnAllFromNearbyTrainerAsync(session, ct);
                        await ReplyAsync(session, n < 0
                            ? "Рядом нет подходящего классового тренера"
                            : $"Выучено абилок: {n}", ct);
                    }
                    return true;

                case "buff" when parts.Length >= 2 && uint.TryParse(parts[1], out var buffSpell):
                    var secs = parts.Length >= 3 && uint.TryParse(parts[2], out var sv) ? sv : 120u;
                    await Auras.ApplyAsync(session, buffSpell, (int)(secs * 1000), positive: true, form: 0, ct);
                    await ReplyAsync(session, $"Бафф {buffSpell} на {secs}с", ct);
                    return true;

                case "unbuff" when parts.Length >= 2 && uint.TryParse(parts[1], out var offSpell):
                    await Auras.RemoveAsync(session, offSpell, ct);
                    await ReplyAsync(session, $"Снят бафф {offSpell}", ct);
                    return true;

                case "dummy":
                    if (session.InWorldGuid == 0)
                        await ReplyAsync(session, "Доступно только в мире", ct);
                    else
                    {
                        await session.World.SummonTrainingDummyAsync(session, ct);
                        await ReplyAsync(session, "Тренировочный манекен перемещён к вам", ct);
                    }
                    return true;

                case "help" or "commands":
                    await ReplyAsync(session, "Команды: .level N | .xp [add] N | .additem ID [count] | .learn SPELL | .learnall | .buff SPELL [сек] | .unbuff SPELL | .dummy", ct);
                    return true;

                default:
                    await ReplyAsync(session, $"Неизвестная команда: .{cmd} (.help)", ct);
                    return true;
            }
        }
        catch (Exception ex)
        {
            session.Logger.LogWarning("DEV команда '.{Cmd}' ошибка: {Msg}", cmd, ex.Message);
            await ReplyAsync(session, $"Ошибка: {ex.Message}", ct);
            return true;
        }
    }

    /// <summary>Выучить спелл без тренера (M9.4/M9.3): персист + грант клиенту (SMSG_LEARNED_SPELL).</summary>
    private static async Task LearnSpellAsync(WorldSession session, uint spellId, CancellationToken ct)
    {
        if (session.InWorldGuid == 0 || !session.KnownSpells.Add(spellId))
            return; // вне мира или уже известен
        await session.CharState.AddLearnedSpellAsync(session.InWorldGuid, spellId, ct);
        await session.SendAsync(WorldOpcode.SmsgLearnedSpell,
            new ByteWriter(6).UInt32(spellId).UInt16(0).ToArray(), ct);
    }

    /// <summary>.xp 500 или .xp add 500.</summary>
    private static bool TryParseXp(string[] parts, out uint amount)
    {
        amount = 0;
        var idx = parts.Length >= 3 && parts[1].Equals("add", StringComparison.OrdinalIgnoreCase) ? 2 : 1;
        return parts.Length > idx && uint.TryParse(parts[idx], out amount);
    }

    /// <summary>Системное сообщение в чат игроку (CHAT_MSG_SYSTEM).</summary>
    private static Task ReplyAsync(WorldSession session, string text, CancellationToken ct)
    {
        var msg = Encoding.UTF8.GetBytes(text);
        var w = new ByteWriter(40 + msg.Length)
            .UInt8(0)                       // CHAT_MSG_SYSTEM
            .UInt32(0)                      // LANG_UNIVERSAL
            .UInt64(0)                      // sender (система)
            .UInt32(0)                      // chat flags
            .UInt64(0)                      // target
            .UInt32((uint)(msg.Length + 1))
            .Bytes(msg).UInt8(0)
            .UInt8(0);                      // chat tag
        return session.SendAsync(WorldOpcode.SmsgMessageChat, w.ToArray(), ct);
    }
}
