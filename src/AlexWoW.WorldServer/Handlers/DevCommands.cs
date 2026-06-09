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

                case "trainer" when parts.Length >= 2:
                    await TrainerCommandAsync(session, parts[1].ToLowerInvariant(), ct);
                    return true;

                case "proftrainer" when parts.Length >= 2:
                    await ProfTrainerCommandAsync(session, parts[1].ToLowerInvariant(), ct);
                    return true;

                case "craft" when parts.Length >= 2:
                    await CraftCommandAsync(session, parts[1].ToLowerInvariant(), ct);
                    return true;

                case "devclean":
                    if (session.InWorldGuid == 0)
                        await ReplyAsync(session, "Доступно только в мире", ct);
                    else
                    {
                        await session.World.DevCleanAsync(session, ct);
                        await ReplyAsync(session, "Все dev-сущности сняты", ct);
                    }
                    return true;

                case "help" or "commands":
                    await ReplyAsync(session, "Команды: .level N | .xp [add] N | .additem ID [count] | .learn SPELL | .learnall | .buff SPELL [сек] | .unbuff SPELL | .dummy | .trainer <class>|off | .proftrainer <prof>|off | .craft anvil|forge|cookfire|mailbox|off | .devclean", ct);
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

    /// <summary>Выучить спелл без тренера (M9.4/M9.3/M10.3): персист + грант клиенту (LEARNED или SUPERCEDED).</summary>
    private static Task LearnSpellAsync(WorldSession session, uint spellId, CancellationToken ct)
        => SpellLearn.GrantAsync(session, spellId, ct);

    /// <summary>Имена классов → id (WotLK). Для <c>.trainer &lt;class&gt;</c>. D1.</summary>
    private static readonly Dictionary<string, byte> ClassByName = new()
    {
        ["warrior"] = 1, ["paladin"] = 2, ["hunter"] = 3, ["rogue"] = 4, ["priest"] = 5,
        ["dk"] = 6, ["deathknight"] = 6, ["shaman"] = 7, ["mage"] = 8, ["warlock"] = 9, ["druid"] = 11,
    };

    /// <summary>
    /// <c>.trainer &lt;class&gt;</c> — поставить классового тренера у игрока (только 1, повтор заменяет);
    /// <c>.trainer off</c> — снять. Entry резолвится data-driven (<c>GetClassTrainerEntryAsync</c>),
    /// спавн — через каркас dev-сущностей (<see cref="World.CreatureDirector.SummonDevNpcAsync"/>). D1.
    /// </summary>
    private static async Task TrainerCommandAsync(WorldSession session, string arg, CancellationToken ct)
    {
        if (session.InWorldGuid == 0)
        {
            await ReplyAsync(session, "Доступно только в мире", ct);
            return;
        }
        if (arg == "off")
        {
            var removed = await session.World.DespawnDevNpcAsync(session, World.DevSlot.Trainer, ct);
            await ReplyAsync(session, removed ? "Тренер снят" : "Тренер не поставлен", ct);
            return;
        }
        if (!ClassByName.TryGetValue(arg, out var classId))
        {
            await ReplyAsync(session, "Класс: warrior/paladin/hunter/rogue/priest/dk/shaman/mage/warlock/druid (или off)", ct);
            return;
        }

        uint? entry;
        try { entry = await session.WorldDb.GetClassTrainerEntryAsync(classId, ct); }
        catch (Exception ex)
        {
            session.Logger.LogDebug("TRAINER cmd: БД мира недоступна ({Msg})", ex.Message);
            await ReplyAsync(session, "БД мира недоступна", ct);
            return;
        }
        if (entry is null)
        {
            await ReplyAsync(session, $"Тренер класса '{arg}' не найден в БД", ct);
            return;
        }

        var ok = await session.World.SummonDevNpcAsync(session, entry.Value, World.DevSlot.Trainer, ct);
        await ReplyAsync(session, ok ? $"Тренер класса '{arg}' поставлен" : "Не удалось поставить тренера", ct);
    }

    /// <summary>Имена профессий → ключевое слово в SubName тренера (нет skill_line_ability). Для
    /// <c>.proftrainer &lt;prof&gt;</c>. D2.</summary>
    private static readonly Dictionary<string, string> ProfKeyword = new()
    {
        ["tailoring"] = "Tailor", ["blacksmithing"] = "Blacksmith", ["leatherworking"] = "Leatherwork",
        ["alchemy"] = "Alchem", ["enchanting"] = "Enchant", ["engineering"] = "Engineer",
        ["jewelcrafting"] = "Jewelcraft", ["mining"] = "Mining", ["herbalism"] = "Herbalism",
        ["skinning"] = "Skinning", ["cooking"] = "Cooking", ["firstaid"] = "First Aid", ["fishing"] = "Fishing",
    };

    /// <summary>
    /// <c>.proftrainer &lt;prof&gt;</c> — поставить тренера профессии у игрока (только 1, повтор заменяет);
    /// <c>.proftrainer off</c> — снять. Реюз каркаса dev-сущностей (D1); резолв entry — по подписи (D2). D2.
    /// </summary>
    private static async Task ProfTrainerCommandAsync(WorldSession session, string arg, CancellationToken ct)
    {
        if (session.InWorldGuid == 0)
        {
            await ReplyAsync(session, "Доступно только в мире", ct);
            return;
        }
        if (arg == "off")
        {
            var removed = await session.World.DespawnDevNpcAsync(session, World.DevSlot.ProfTrainer, ct);
            await ReplyAsync(session, removed ? "Тренер профессии снят" : "Тренер профессии не поставлен", ct);
            return;
        }
        if (!ProfKeyword.TryGetValue(arg, out var keyword))
        {
            await ReplyAsync(session, "Профессия: tailoring/blacksmithing/leatherworking/alchemy/enchanting/engineering/jewelcrafting/mining/herbalism/skinning/cooking/firstaid/fishing (или off)", ct);
            return;
        }

        uint? entry;
        try { entry = await session.WorldDb.GetProfessionTrainerEntryAsync(keyword, ct); }
        catch (Exception ex)
        {
            session.Logger.LogDebug("PROFTRAINER cmd: БД мира недоступна ({Msg})", ex.Message);
            await ReplyAsync(session, "БД мира недоступна", ct);
            return;
        }
        if (entry is null)
        {
            await ReplyAsync(session, $"Тренер профессии '{arg}' не найден в БД", ct);
            return;
        }

        var ok = await session.World.SummonDevNpcAsync(session, entry.Value, World.DevSlot.ProfTrainer, ct);
        await ReplyAsync(session, ok ? $"Тренер профессии '{arg}' поставлен" : "Не удалось поставить тренера", ct);
    }

    /// <summary>Крафт-станки/почта → канонические entry гейм-объектов (стабильные мировые GO). Anvil/Forge/
    /// Campfire — spell-focus (type 8) той же группы, что использует клиент для «вы рядом с …»; Mailbox — type 19.
    /// D3.</summary>
    private static readonly Dictionary<string, uint> CraftGo = new()
    {
        ["anvil"] = 1744,      // Anvil (spell-focus 1)
        ["forge"] = 1685,      // Forge (spell-focus 3)
        ["cookfire"] = 1798,   // Campfire (spell-focus 4 — кулинария)
        ["mailbox"] = 32349,   // Mailbox (type 19)
    };

    /// <summary>
    /// <c>.craft anvil|forge|cookfire|mailbox</c> — поставить крафт-станок/почту (гейм-объект) у игрока
    /// (один на тип, повтор заменяет); <c>.craft off</c> — снять все dev-станки. Реюз каркаса D1/D3. D3.
    /// </summary>
    private static async Task CraftCommandAsync(WorldSession session, string arg, CancellationToken ct)
    {
        if (session.InWorldGuid == 0)
        {
            await ReplyAsync(session, "Доступно только в мире", ct);
            return;
        }
        if (arg == "off")
        {
            await session.World.DevCleanGosAsync(session, ct);
            await ReplyAsync(session, "Dev-станки сняты", ct);
            return;
        }
        if (!CraftGo.TryGetValue(arg, out var entry))
        {
            await ReplyAsync(session, "Станок: anvil/forge/cookfire/mailbox (или off)", ct);
            return;
        }

        var ok = await session.World.SummonDevGoAsync(session, entry, arg, ct);
        await ReplyAsync(session, ok ? $"Станок '{arg}' поставлен" : "Не удалось поставить станок", ct);
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
