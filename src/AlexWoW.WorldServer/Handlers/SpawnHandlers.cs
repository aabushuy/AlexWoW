using AlexWoW.Common.Network;
using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Models;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Запросы шаблонов существ и гейм-объектов (M5; DI-модуль M7 S7): CMSG_CREATURE_QUERY и
/// CMSG_GAMEOBJECT_QUERY. Источник — БД мира CMaNGOS (creature_template/gameobject_template);
/// при её недоступности — fallback на тестовый реестр <see cref="Npcs"/>. Пересчёт видимых
/// NPC/GO вынесен в <see cref="World.VisibilityService"/> (S7).
/// </summary>
internal sealed class SpawnHandlers(IWorldRepository worldDb) : IOpcodeHandlerModule
{
    [WorldOpcodeHandler(WorldOpcode.CmsgGameObjectQuery)]
    public async Task OnGameObjectQuery(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var reader = packet.Reader();
        var entry = reader.UInt32();
        // далее u64 guid — для ответа не нужен.

        GameObjectTemplateData? t = null;
        try { t = await worldDb.GetGameObjectTemplateAsync(entry, ct); }
        catch { /* БД недоступна */ }

        if (t is null)
        {
            await session.SendAsync(WorldOpcode.SmsgGameObjectQueryResponse,
                new ByteWriter(4).UInt32(entry | 0x80000000u).ToArray(), ct);
            return;
        }

        var w = new ByteWriter(160);
        w.UInt32(t.Entry).UInt32(t.Type).UInt32(t.DisplayId);
        w.CString(t.Name).CString(string.Empty).CString(string.Empty).CString(string.Empty); // name[0..3]
        w.CString(t.IconName ?? string.Empty)
         .CString(t.CastBarCaption ?? string.Empty)
         .CString(t.Unk1 ?? string.Empty);
        for (var i = 0; i < 24; i++) w.UInt32(0); // data[0..23] (поведение GO — пока не нужно)
        w.Single(t.Size <= 0 ? 1.0f : t.Size);
        for (var i = 0; i < 6; i++) w.UInt32(0);  // quest items
        await session.SendAsync(WorldOpcode.SmsgGameObjectQueryResponse, w.ToArray(), ct);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgCreatureQuery)]
    public async Task OnCreatureQuery(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var reader = packet.Reader();
        var entry = reader.UInt32();
        // далее u64 guid — для ответа не нужен.

        var template = await ResolveTemplateAsync(session, entry, ct);
        if (template is null)
        {
            await session.SendAsync(WorldOpcode.SmsgCreatureQueryResponse,
                new ByteWriter(4).UInt32(entry | 0x80000000u).ToArray(), ct);
            return;
        }

        await session.SendAsync(WorldOpcode.SmsgCreatureQueryResponse, BuildQueryResponse(template), ct);
    }

    private async Task<CreatureTemplate?> ResolveTemplateAsync(WorldSession session, uint entry, CancellationToken ct)
    {
        try
        {
            var t = await worldDb.GetCreatureTemplateAsync(entry, ct);
            if (t is not null)
            {
                return new CreatureTemplate(t.Entry, t.Name, t.SubName ?? string.Empty, t.DisplayId1,
                    t.MinLevel, t.Faction, t.CreatureType, t.Scale, t.NpcFlags, t.UnitClass);
            }
        }
        catch (Exception ex)
        {
            session.Logger.LogDebug(ex, "CREATURE_QUERY {Entry}: БД мира недоступна ({Msg})", entry, ex.Message);
        }
        return Npcs.Find(entry); // fallback на тестовый реестр
    }

    /// <summary>SMSG_CREATURE_QUERY_RESPONSE (3.3.5a). Layout сверен с gtker.com.</summary>
    private static byte[] BuildQueryResponse(CreatureTemplate t)
    {
        var w = new ByteWriter(128);
        w.UInt32(t.Entry);
        w.CString(t.Name)              // name[0]
         .CString(string.Empty)        // name[1]
         .CString(string.Empty)        // name[2]
         .CString(string.Empty)        // name[3]
         .CString(t.SubName)           // подпись (title под именем)
         .CString(string.Empty);       // icon ("Directions" и т.п.)
        w.UInt32(0)                    // type_flags
         .UInt32(t.UnitType)           // creature_type
         .UInt32(0)                    // family
         .UInt32(0)                    // rank
         .UInt32(0)                    // kill_credit1
         .UInt32(0);                   // kill_credit2
        for (var i = 0; i < 4; i++)    // display_ids[4]
            w.UInt32(i == 0 ? t.DisplayId : 0);
        w.Single(1.0f)                 // health_multiplier
         .Single(1.0f)                 // mana_multiplier
         .UInt8(0);                    // racial_leader
        for (var i = 0; i < 6; i++)    // quest_items[6]
            w.UInt32(0);
        w.UInt32(0);                   // movement_id
        return w.ToArray();
    }
}
