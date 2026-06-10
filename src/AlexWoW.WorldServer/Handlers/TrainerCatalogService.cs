using System.Text;
using AlexWoW.Common.Network;
using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Models;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Каталог тренеров (M9.3, DI-сервис M7 S6 — логика из TrainerHandlers): построение меню/списка абилок
/// (SMSG_GOSSIP_MESSAGE / SMSG_TRAINER_LIST), гейтинг по классу/расе/уровню/навыку и покупка
/// (списать деньги → выучить через <see cref="SpellLearnService"/>). Ассортимент/требования — из
/// npc_trainer (дамп CMaNGOS). Парсер Spell.dbc не нужен — клиент рисует книгу/каст сам.
/// </summary>
internal sealed class TrainerCatalogService(
    SpellLearnService spellLearn,
    IWorldRepository worldDb,
    ICharacterRepository characters)
{
    // TrainerSpellState (wow_messages 3.3.5): GREEN=доступен, RED=недоступен, GRAY=уже изучен.
    private const byte StateGreen = 0;
    private const byte StateRed = 1;
    private const byte StateGray = 2;

    // TrainingFailureReason (SMSG_TRAINER_BUY_FAILED): печатается только в консоль клиента.
    private const uint FailUnavailable = 0;
    private const uint FailNotEnoughMoney = 1;

    /// <summary>UNIT_NPC_FLAG_TRAINER (0x10) — базовый флаг любого тренера.</summary>
    private const uint NpcFlagTrainer = 0x10;

    /// <summary>GOSSIP_ICON_TRAINER (3) — иконка-«книга» у пункта меню тренера.</summary>
    private const byte GossipIconTrainer = 3;
    /// <summary>Id пункта «обучиться» в меню госсипа тренера.</summary>
    internal const uint TrainGossipOptionId = 0;
    /// <summary>Id пункта «сбросить таланты» (только у классового тренера). M9.8.</summary>
    internal const uint ResetTalentsOptionId = 1;
    /// <summary>GOSSIP_ICON_CHAT (0) — обычная иконка-«реплика».</summary>
    private const byte GossipIconChat = 0;
    /// <summary>CMaNGOS DEFAULT_GOSSIP_MESSAGE — дефолтный npc_text для greeting'а меню (своего у нас нет).</summary>
    private const uint DefaultGossipTextId = 0xFFFFFF;

    /// <summary>entry шаблона существа из его GUID (0xF130 | entry&lt;&lt;24 | counter).</summary>
    private static uint CreatureEntry(ulong guid) => (uint)((guid >> 24) & 0xFFFFFF);

    /// <summary>
    /// Хук госсипа (<see cref="GossipService.OnHelloAsync"/>): если NPC — тренер, подходящий игроку, шлёт МЕНЮ госсипа с
    /// пунктом «обучиться» (SMSG_GOSSIP_MESSAGE). У тренеров стоит флаг GOSSIP — клиент ждёт меню и
    /// игнорирует прямой SMSG_TRAINER_LIST; список абилок шлём уже на выбор пункта (см.
    /// <see cref="TrainerHandlers.OnGossipSelect"/>). Возвращает true, если меню отправлено (приоритет над вендором). M9.3.
    /// </summary>
    internal async Task<bool> TrySendTrainerGossipAsync(WorldSession session, ulong npcGuid, CancellationToken ct)
    {
        var entry = CreatureEntry(npcGuid);
        TrainerData? trainer;
        try { trainer = await worldDb.GetTrainerAsync(entry, ct); }
        catch (Exception ex)
        {
            session.Logger.LogDebug(ex, "TRAINER gossip {Entry}: БД мира недоступна ({Msg})", entry, ex.Message);
            return false;
        }
        if (trainer is null || !FitsPlayer(session, trainer))
            return false;

        var learn = Encoding.UTF8.GetBytes("Я хочу обучиться.");
        var reset = Encoding.UTF8.GetBytes("Я хочу сбросить таланты.");
        var showReset = trainer.TrainerType == 0; // сброс талантов — только у классового тренера (M9.8)
        var w = new ByteWriter(96 + learn.Length + reset.Length)
            .UInt64(npcGuid)
            .UInt32(0)                                  // menu_id
            .UInt32(DefaultGossipTextId)                // title_text_id (greeting)
            .UInt32((uint)(showReset ? 2 : 1));         // amount_of_gossip_items
        w.UInt32(TrainGossipOptionId)        // пункт «обучиться»
            .UInt8(GossipIconTrainer).UInt8(0).UInt32(0)
            .Bytes(learn).UInt8(0).UInt8(0);
        if (showReset)
        {
            w.UInt32(ResetTalentsOptionId)   // пункт «сбросить таланты»
                .UInt8(GossipIconChat).UInt8(0).UInt32(0)
                .Bytes(reset).UInt8(0).UInt8(0);
        }

        w.UInt32(0);                         // amount_of_quests
        await session.SendAsync(WorldOpcode.SmsgGossipMessage, w.ToArray(), ct);
        session.Logger.LogDebug("TRAINER gossip entry={Entry}: меню «обучиться» (класс {Class})",
            entry, trainer.TrainerClass);
        return true;
    }

    /// <summary>
    /// Шлёт окно тренера (SMSG_TRAINER_LIST), если NPC — тренер, подходящий игроку по классу/расе.
    /// Возвращает true, если окно отправлено. M9.3.
    /// </summary>
    internal async Task<bool> SendTrainerListAsync(WorldSession session, ulong npcGuid, CancellationToken ct)
    {
        var entry = CreatureEntry(npcGuid);
        TrainerData? trainer;
        try { trainer = await worldDb.GetTrainerAsync(entry, ct); }
        catch (Exception ex)
        {
            session.Logger.LogDebug(ex, "TRAINER_LIST {Entry}: БД мира недоступна ({Msg})", entry, ex.Message);
            return false;
        }
        if (trainer is null || !FitsPlayer(session, trainer))
            return false;

        var c = session.Character;
        if (c is null)
            return false;

        var w = new ByteWriter(64 + trainer.Spells.Count * 38);
        w.UInt64(npcGuid);
        w.UInt32(trainer.TrainerType);
        w.UInt32((uint)trainer.Spells.Count);
        foreach (var s in trainer.Spells)
        {
            w.UInt32(s.Spell);
            w.UInt8(StateFor(session, c.Level, s));
            w.UInt32(s.SpellCost);
            w.UInt32(0);                       // talent_point_cost — спеллы не стоят очков талантов
            w.UInt32(0);                       // first_rank (профессии) — у нас всегда 0
            w.UInt8((byte)EffectiveReqLevel(s));
            w.UInt32(s.ReqSkill);
            w.UInt32(s.ReqSkillValue);
            w.UInt32(s.ReqAbility1);
            w.UInt32(s.ReqAbility2);
            w.UInt32(s.ReqAbility3);
        }
        var greeting = Encoding.UTF8.GetBytes(trainer.Greeting);
        w.Bytes(greeting).UInt8(0);            // CString greeting

        await session.SendAsync(WorldOpcode.SmsgTrainerList, w.ToArray(), ct);
        session.Logger.LogDebug("TRAINER_LIST entry={Entry}: {Count} абилок (тип {Type}, класс {Class})",
            entry, trainer.Spells.Count, trainer.TrainerType, trainer.TrainerClass);
        return true;
    }

    /// <summary>Покупка абилки у тренера (CMSG_TRAINER_BUY_SPELL): гейтинг GREEN → списание денег →
    /// грант (LEARNED/SUPERCEDED, M10.3) → SMSG_TRAINER_BUY_SUCCEEDED. M9.3.</summary>
    internal async Task BuySpellAsync(WorldSession session, ulong npcGuid, uint spellId, CancellationToken ct)
    {
        var entry = CreatureEntry(npcGuid);

        async Task FailAsync(uint reason) => await session.SendAsync(WorldOpcode.SmsgTrainerBuyFailed,
            new ByteWriter(16).UInt64(npcGuid).UInt32(spellId).UInt32(reason).ToArray(), ct);

        var c = session.Character;
        if (c is null || session.InWorldGuid == 0)
            return;

        TrainerData? trainer;
        try { trainer = await worldDb.GetTrainerAsync(entry, ct); }
        catch { await FailAsync(FailUnavailable); return; }
        if (trainer is null || !FitsPlayer(session, trainer))
        {
            await FailAsync(FailUnavailable);
            return;
        }

        var spell = trainer.Spells.FirstOrDefault(s => s.Spell == spellId);
        if (spell is null || StateFor(session, c.Level, spell) != StateGreen)
        {
            await FailAsync(FailUnavailable); // нет в списке / не доступна (уровень/известна/предусловие)
            return;
        }
        if (session.Inv.Money < spell.SpellCost)
        {
            await FailAsync(FailNotEnoughMoney);
            return;
        }

        // Списать деньги.
        if (spell.SpellCost > 0)
        {
            session.Inv.Money -= spell.SpellCost;
            await characters.SetMoneyAsync(session.InWorldGuid, session.Inv.Money, ct);
            await session.SendAsync(WorldOpcode.SmsgUpdateObject,
                PlayerSpawn.BuildCoinageUpdate((ulong)session.InWorldGuid, session.Inv.Money), ct);
        }

        // Выучить: персист + клиентский грант (LEARNED, либо SUPERCEDED для высшего ранга). M10.3.
        await spellLearn.GrantAsync(session, spellId, ct);
        await session.SendAsync(WorldOpcode.SmsgTrainerBuySucceeded,
            new ByteWriter(12).UInt64(npcGuid).UInt32(spellId).ToArray(), ct);
        session.Logger.LogInformation("TRAINER BUY '{User}': spell={Spell} за {Cost}, осталось {Money}",
            session.Account, spellId, spell.SpellCost, session.Inv.Money);
    }

    /// <summary>Подходит ли тренер игроку: классовый — только своему классу; расовый гейт, если задан. M9.3.</summary>
    private static bool FitsPlayer(WorldSession session, TrainerData trainer)
    {
        var c = session.Character;
        if (c is null)
            return false;
        // TrainerType 0 = классовый тренер: класс должен совпадать (0 = без гейта по классу).
        if (trainer.TrainerType == 0 && trainer.TrainerClass != 0 && trainer.TrainerClass != c.Class)
            return false;
        if (trainer.TrainerRace != 0 && trainer.TrainerRace != c.Race)
            return false;
        return true;
    }

    /// <summary>
    /// Состояние абилки у тренера (M9.3, упрощённо без Spell.dbc): известна → GRAY; не хватает уровня
    /// или незнакома требуемая предыдущая абилка (ReqAbility) → RED; иначе GREEN. Skill-требования
    /// (профессии) пока не проверяем — классовые абилки их не используют.
    /// </summary>
    /// <summary>
    /// Уровень изучения абилки: max(npc_trainer.reqlevel, Spell.dbc spellLevel). В дампе reqlevel почти
    /// всегда 0, реальный уровень ранга — в spell_template.SpellLevel (#27). Без этого все ранги были бы
    /// доступны на 1 уровне.
    /// </summary>
    private static uint EffectiveReqLevel(TrainerSpell s) => Math.Max((uint)s.ReqLevel, s.SpellLevel);

    private static byte StateFor(WorldSession session, byte level, TrainerSpell s)
    {
        if (session.Progression.KnownSpells.Contains(s.Spell))
            return StateGray;
        if (level < EffectiveReqLevel(s))
            return StateRed;
        if ((s.ReqAbility1 != 0 && !session.Progression.KnownSpells.Contains(s.ReqAbility1))
            || (s.ReqAbility2 != 0 && !session.Progression.KnownSpells.Contains(s.ReqAbility2))
            || (s.ReqAbility3 != 0 && !session.Progression.KnownSpells.Contains(s.ReqAbility3)))
        {
            return StateRed;
        }
        // M11: гейт по навыку профессии — рецепт недоступен, если навык игрока ниже требуемого
        // (reqskill/reqskillvalue из npc_trainer). Теперь у нас есть навыки (M11.1).
        if (s.ReqSkill != 0)
        {
            var sk = session.Progression.SkillBook.Get((ushort)s.ReqSkill);
            if (sk is null || sk.Value < s.ReqSkillValue)
                return StateRed;
        }
        return StateGreen;
    }

    /// <summary>Несёт ли NPC флаг тренера (UNIT_NPC_FLAG_TRAINER). Для дешёвого хука госсипа. M9.3.</summary>
    internal bool IsTrainerNpc(WorldSession session, ulong npcGuid)
        => session.Visibility.VisibleNpcs.TryGetValue(npcGuid, out var creature)
           && (creature.Template.NpcFlags & NpcFlagTrainer) != 0;

    /// <summary>
    /// Дев-команда <c>.learnall</c> (M-абилки): учит ВСЕ доступные по текущему уровню (состояние GREEN)
    /// абилки у ближайшего подходящего классового тренера — без списания денег. Переиспользует гейтинг
    /// <see cref="StateFor"/>/<see cref="FitsPlayer"/> (только разрешённые уровнем/предусловиями ранги).
    /// Возвращает число выученных, либо -1, если рядом нет подходящего тренера. M-абилки (.learnall).
    /// </summary>
    internal async Task<int> LearnAllFromNearbyTrainerAsync(WorldSession session, CancellationToken ct)
    {
        var c = session.Character;
        if (c is null || session.InWorldGuid == 0)
            return -1;

        // Среди видимых тренеров найти первого, подходящего игроку по классу/расе.
        TrainerData? trainer = null;
        foreach (var creature in session.Visibility.VisibleNpcs.Values)
        {
            if ((creature.Template.NpcFlags & NpcFlagTrainer) == 0)
                continue;
            TrainerData? t;
            try { t = await worldDb.GetTrainerAsync(CreatureEntry(creature.Guid), ct); }
            catch { continue; }
            if (t is not null && FitsPlayer(session, t))
            {
                trainer = t;
                break;
            }
        }
        if (trainer is null)
            return -1;

        // По возрастанию уровня изучения — чтобы низший ранг учился раньше высшего и SUPERCEDED шёл цепочкой. M10.3.
        var learned = 0;
        foreach (var s in trainer.Spells.OrderBy(x => x.SpellLevel))
        {
            if (StateFor(session, c.Level, s) != StateGreen)
                continue;
            if (await spellLearn.GrantAsync(session, s.Spell, ct))
                learned++;
        }
        session.Logger.LogInformation("LEARNALL '{User}': выучено {Count} абилок (класс {Class}, ур.{Level})",
            session.Account, learned, c.Class, c.Level);
        return learned;
    }
}
