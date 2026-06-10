using AlexWoW.Common.Network;
using AlexWoW.Database.Abstractions;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Опкод-входы тренеров (M9.3, DI-модуль M7 S6): тонкий парсинг CMSG_TRAINER_LIST /
/// CMSG_GOSSIP_SELECT_OPTION / CMSG_NPC_TEXT_QUERY / CMSG_TRAINER_BUY_SPELL и делегирование в
/// <see cref="TrainerCatalogService"/> (списки/гейтинг/покупка) и <see cref="TalentHandlers"/> (сброс талантов).
/// </summary>
internal sealed class TrainerHandlers(
    TrainerCatalogService catalog,
    TalentHandlers talents,
    IWorldRepository worldDb) : IOpcodeHandlerModule
{
    /// <summary>entry шаблона существа из его GUID (0xF130 | entry&lt;&lt;24 | counter).</summary>
    private static uint CreatureEntry(ulong guid) => (uint)((guid >> 24) & 0xFFFFFF);

    [WorldOpcodeHandler(WorldOpcode.CmsgTrainerList)]
    public async Task OnTrainerList(WorldSession session, IncomingPacket packet, CancellationToken ct)
        => await catalog.SendTrainerListAsync(session, packet.Reader().UInt64(), ct);

    /// <summary>
    /// CMSG_GOSSIP_SELECT_OPTION: игрок выбрал пункт меню. Единственный пункт у тренера — «обучиться» →
    /// шлём список абилок. M9.3. (Другие меню госсипа пока не используем.)
    /// </summary>
    [WorldOpcodeHandler(WorldOpcode.CmsgGossipSelectOption)]
    public async Task OnGossipSelect(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var r = packet.Reader();
        var npcGuid = r.UInt64();
        r.UInt32();                      // menu_id — не используем
        var optionId = r.UInt32();       // gossip_list_id
        if (optionId == TrainerCatalogService.TrainGossipOptionId)
            await catalog.SendTrainerListAsync(session, npcGuid, ct);
        else if (optionId == TrainerCatalogService.ResetTalentsOptionId)
            await talents.SendWipeConfirmAsync(session, npcGuid, ct); // M9.8
    }

    /// <summary>
    /// CMSG_NPC_TEXT_QUERY → SMSG_NPC_TEXT_UPDATE (M9.3): клиент, получив меню госсипа, запрашивает текст
    /// greeting'а по title_text_id и НЕ рисует меню, пока не получит ответ. Шлём 8 блоков (как требует
    /// формат); заполняем только блок 0 (probability=1.0, текст = greeting тренера или дефолт), остальные
    /// нулевые. Без этого ответа меню тренера не открывается. NpcTextUpdate = f32 prob + CString[2] +
    /// u32 lang + 3×(u32 delay,u32 emote).
    /// </summary>
    [WorldOpcodeHandler(WorldOpcode.CmsgNpcTextQuery)]
    public async Task OnNpcTextQuery(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var r = packet.Reader();
        var textId = r.UInt32();
        var npcGuid = r.UInt64();

        var greeting = "Чем я могу помочь?";
        try
        {
            var trainer = await worldDb.GetTrainerAsync(CreatureEntry(npcGuid), ct);
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

    [WorldOpcodeHandler(WorldOpcode.CmsgTrainerBuySpell)]
    public async Task OnBuySpell(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var r = packet.Reader();
        var npcGuid = r.UInt64();
        var spellId = r.UInt32();
        await catalog.BuySpellAsync(session, npcGuid, spellId, ct);
    }
}
