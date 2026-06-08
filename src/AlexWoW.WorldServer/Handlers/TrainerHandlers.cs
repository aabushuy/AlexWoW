using System.Text;
using AlexWoW.Common.Network;
using AlexWoW.Database.Models;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Тренеры классов (M9.3): список изучаемых абилок (CMSG_TRAINER_LIST → SMSG_TRAINER_LIST) и покупка
/// (CMSG_TRAINER_BUY_SPELL → списать деньги, добавить в character_spell, SMSG_LEARNED_SPELL +
/// SMSG_TRAINER_BUY_SUCCEEDED). Ассортимент/требования — из npc_trainer (дамп CMaNGOS). Гейтинг абилки:
/// уже известна → GRAY, не хватает уровня/предыдущей абилки → RED, иначе GREEN. Парсер Spell.dbc не нужен —
/// клиент рисует книгу/каст сам; spell-цепочки рангов (SUPERCEDED) и профессии — отложены.
/// </summary>
public static class TrainerHandlers
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
    /// <summary>Id пункта «обучиться» в меню госсипа тренера (у нас единственный пункт).</summary>
    private const uint TrainGossipOptionId = 0;
    /// <summary>CMaNGOS DEFAULT_GOSSIP_MESSAGE — дефолтный npc_text для greeting'а меню (своего у нас нет).</summary>
    private const uint DefaultGossipTextId = 0xFFFFFF;

    /// <summary>entry шаблона существа из его GUID (0xF130 | entry&lt;&lt;24 | counter).</summary>
    private static uint CreatureEntry(ulong guid) => (uint)((guid >> 24) & 0xFFFFFF);

    [WorldOpcodeHandler(WorldOpcode.CmsgTrainerList)]
    public static async Task OnTrainerList(WorldSession session, IncomingPacket packet, CancellationToken ct)
        => await SendTrainerListAsync(session, packet.Reader().UInt64(), ct);

    /// <summary>
    /// CMSG_GOSSIP_SELECT_OPTION: игрок выбрал пункт меню. Единственный пункт у тренера — «обучиться» →
    /// шлём список абилок. M9.3. (Другие меню госсипа пока не используем.)
    /// </summary>
    [WorldOpcodeHandler(WorldOpcode.CmsgGossipSelectOption)]
    public static async Task OnGossipSelect(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var r = packet.Reader();
        var npcGuid = r.UInt64();
        r.UInt32();                      // menu_id — не используем
        var optionId = r.UInt32();       // gossip_list_id
        if (optionId == TrainGossipOptionId)
            await SendTrainerListAsync(session, npcGuid, ct);
    }

    /// <summary>
    /// CMSG_NPC_TEXT_QUERY → SMSG_NPC_TEXT_UPDATE (M9.3): клиент, получив меню госсипа, запрашивает текст
    /// greeting'а по title_text_id и НЕ рисует меню, пока не получит ответ. Шлём 8 блоков (как требует
    /// формат); заполняем только блок 0 (probability=1.0, текст = greeting тренера или дефолт), остальные
    /// нулевые. Без этого ответа меню тренера не открывается. NpcTextUpdate = f32 prob + CString[2] +
    /// u32 lang + 3×(u32 delay,u32 emote).
    /// </summary>
    [WorldOpcodeHandler(WorldOpcode.CmsgNpcTextQuery)]
    public static async Task OnNpcTextQuery(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var r = packet.Reader();
        var textId = r.UInt32();
        var npcGuid = r.UInt64();

        var greeting = "Чем я могу помочь?";
        try
        {
            var trainer = await session.WorldDb.GetTrainerAsync(CreatureEntry(npcGuid), ct);
            if (trainer is { Greeting.Length: > 0 })
                greeting = trainer.Greeting;
        }
        catch { /* БД мира недоступна — дефолтный greeting */ }

        var text = System.Text.Encoding.UTF8.GetBytes(greeting);
        var w = new ByteWriter(64 + text.Length * 2 + 8 * 34);
        w.UInt32(textId);
        for (var i = 0; i < 8; i++)
        {
            if (i == 0)
            {
                w.Single(1.0f);                       // probability
                w.Bytes(text).UInt8(0);               // text[0] (male)
                w.Bytes(text).UInt8(0);               // text[1] (female)
            }
            else
            {
                w.Single(0f);
                w.UInt8(0).UInt8(0);                  // обе CString пустые
            }
            w.UInt32(0);                              // language (Universal)
            for (var e = 0; e < 3; e++)
                w.UInt32(0).UInt32(0);                // emote: delay + emote
        }
        await session.SendAsync(WorldOpcode.SmsgNpcTextUpdate, w.ToArray(), ct);
    }

    /// <summary>
    /// Хук госсипа (QuestHandlers.OnHello): если NPC — тренер, подходящий игроку, шлёт МЕНЮ госсипа с
    /// пунктом «обучиться» (SMSG_GOSSIP_MESSAGE). У тренеров стоит флаг GOSSIP — клиент ждёт меню и
    /// игнорирует прямой SMSG_TRAINER_LIST; список абилок шлём уже на выбор пункта (см.
    /// <see cref="OnGossipSelect"/>). Возвращает true, если меню отправлено (приоритет над вендором). M9.3.
    /// </summary>
    internal static async Task<bool> TrySendTrainerGossipAsync(WorldSession session, ulong npcGuid, CancellationToken ct)
    {
        var entry = CreatureEntry(npcGuid);
        TrainerData? trainer;
        try { trainer = await session.WorldDb.GetTrainerAsync(entry, ct); }
        catch (Exception ex)
        {
            session.Logger.LogDebug("TRAINER gossip {Entry}: БД мира недоступна ({Msg})", entry, ex.Message);
            return false;
        }
        if (trainer is null || !FitsPlayer(session, trainer))
            return false;

        var option = System.Text.Encoding.UTF8.GetBytes("Я хочу обучиться.");
        var w = new ByteWriter(64 + option.Length)
            .UInt64(npcGuid)
            .UInt32(0)                       // menu_id
            .UInt32(DefaultGossipTextId)     // title_text_id (greeting)
            .UInt32(1);                      // amount_of_gossip_items
        w.UInt32(TrainGossipOptionId)        // id пункта
            .UInt8(GossipIconTrainer)        // иконка-книга
            .UInt8(0)                        // coded = false
            .UInt32(0)                       // money_required
            .Bytes(option).UInt8(0)          // message (CString)
            .UInt8(0);                       // accept_text (пустая CString)
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
    internal static async Task<bool> SendTrainerListAsync(WorldSession session, ulong npcGuid, CancellationToken ct)
    {
        var entry = CreatureEntry(npcGuid);
        TrainerData? trainer;
        try { trainer = await session.WorldDb.GetTrainerAsync(entry, ct); }
        catch (Exception ex)
        {
            session.Logger.LogDebug("TRAINER_LIST {Entry}: БД мира недоступна ({Msg})", entry, ex.Message);
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
            w.UInt8(s.ReqLevel);
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

    [WorldOpcodeHandler(WorldOpcode.CmsgTrainerBuySpell)]
    public static async Task OnBuySpell(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var r = packet.Reader();
        var npcGuid = r.UInt64();
        var spellId = r.UInt32();
        var entry = CreatureEntry(npcGuid);

        async Task FailAsync(uint reason) => await session.SendAsync(WorldOpcode.SmsgTrainerBuyFailed,
            new ByteWriter(16).UInt64(npcGuid).UInt32(spellId).UInt32(reason).ToArray(), ct);

        var c = session.Character;
        if (c is null || session.InWorldGuid == 0)
            return;

        TrainerData? trainer;
        try { trainer = await session.WorldDb.GetTrainerAsync(entry, ct); }
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
        if (session.Money < spell.SpellCost)
        {
            await FailAsync(FailNotEnoughMoney);
            return;
        }

        // Списать деньги.
        if (spell.SpellCost > 0)
        {
            session.Money -= spell.SpellCost;
            await session.Characters.SetMoneyAsync(session.InWorldGuid, session.Money, ct);
            await session.SendAsync(WorldOpcode.SmsgUpdateObject,
                PlayerSpawn.BuildCoinageUpdate((ulong)session.InWorldGuid, session.Money), ct);
        }

        // Выучить: персист + клиентский грант (SMSG_LEARNED_SPELL добавляет абилку в книгу).
        session.KnownSpells.Add(spellId);
        await session.Characters.AddLearnedSpellAsync(session.InWorldGuid, spellId, ct);
        await session.SendAsync(WorldOpcode.SmsgLearnedSpell,
            new ByteWriter(6).UInt32(spellId).UInt16(0).ToArray(), ct);
        await session.SendAsync(WorldOpcode.SmsgTrainerBuySucceeded,
            new ByteWriter(12).UInt64(npcGuid).UInt32(spellId).ToArray(), ct);
        session.Logger.LogInformation("TRAINER BUY '{User}': spell={Spell} за {Cost}, осталось {Money}",
            session.Account, spellId, spell.SpellCost, session.Money);
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
    private static byte StateFor(WorldSession session, byte level, TrainerSpell s)
    {
        if (session.KnownSpells.Contains(s.Spell))
            return StateGray;
        if (level < s.ReqLevel)
            return StateRed;
        if ((s.ReqAbility1 != 0 && !session.KnownSpells.Contains(s.ReqAbility1))
            || (s.ReqAbility2 != 0 && !session.KnownSpells.Contains(s.ReqAbility2))
            || (s.ReqAbility3 != 0 && !session.KnownSpells.Contains(s.ReqAbility3)))
            return StateRed;
        return StateGreen;
    }

    /// <summary>Несёт ли NPC флаг тренера (UNIT_NPC_FLAG_TRAINER). Для дешёвого хука госсипа. M9.3.</summary>
    internal static bool IsTrainerNpc(WorldSession session, ulong npcGuid)
        => session.VisibleNpcs.TryGetValue(npcGuid, out var creature)
           && (creature.Template.NpcFlags & NpcFlagTrainer) != 0;
}
