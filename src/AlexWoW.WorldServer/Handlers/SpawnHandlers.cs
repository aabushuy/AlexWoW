using AlexWoW.Common.Network;
using AlexWoW.Database.Models;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Видимость и существа (M5): спавн NPC у клиента (SMSG_UPDATE_OBJECT) и ответ на
/// CMSG_CREATURE_QUERY. Источник — БД мира CMaNGOS (creature/creature_template);
/// при её недоступности — fallback на один захардкоженный тестовый NPC.
/// </summary>
public static class SpawnHandlers
{
    /// <summary>Сколько существ максимум показываем за один вход (защита от перегруза в городах).</summary>
    private const int MaxNearbyNpcs = 100;

    /// <summary>Спавнит существ из БД мира рядом с игроком (или тестового NPC, если БД нет).</summary>
    internal static async Task SendNearbyNpcsAsync(WorldSession session, Character character, CancellationToken ct)
    {
        IReadOnlyList<CreatureSpawnData> rows;
        try
        {
            rows = await session.WorldDb.GetCreaturesNearAsync(
                character.Map, character.X, character.Y, World.WorldState.VisibilityRange, MaxNearbyNpcs, ct);
        }
        catch (Exception ex)
        {
            session.Logger.LogWarning("БД мира недоступна при спавне NPC ({Msg}) — показываю тестового", ex.Message);
            await SendTestNpcAsync(session, character, ct);
            return;
        }

        var time = (uint)Environment.TickCount;
        var shown = 0;
        foreach (var row in rows)
        {
            if (row.DisplayId == 0)
                continue; // нет модели — пропускаем (модель задаётся иначе, не отрисуем)

            var template = new CreatureTemplate(
                row.Entry, row.Name, row.SubName ?? string.Empty, row.DisplayId,
                row.MinLevel, row.Faction, row.CreatureType, row.Scale, row.NpcFlags, row.UnitClass);
            var spawn = new NpcSpawn(Npcs.UnitGuid(row.Entry, row.Guid), template, row.X, row.Y, row.Z, row.O);

            session.VisibleNpcs[spawn.Guid] = spawn;
            await session.SendAsync(WorldOpcode.SmsgUpdateObject, CreatureUpdate.BuildCreateObject(spawn, time), ct);
            shown++;
        }

        session.Logger.LogInformation("Спавн {Count} NPC из БД мира рядом с '{User}' (map={Map})",
            shown, session.Account, character.Map);
    }

    private static async Task SendTestNpcAsync(WorldSession session, Character character, CancellationToken ct)
    {
        var spawn = new NpcSpawn(
            Npcs.UnitGuid(Npcs.TestDummy.Entry, counter: 1), Npcs.TestDummy,
            character.X + 4f, character.Y, character.Z, MathF.PI);
        session.VisibleNpcs[spawn.Guid] = spawn;
        await session.SendAsync(WorldOpcode.SmsgUpdateObject,
            CreatureUpdate.BuildCreateObject(spawn, (uint)Environment.TickCount), ct);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgCreatureQuery)]
    public static async Task OnCreatureQuery(WorldSession session, IncomingPacket packet, CancellationToken ct)
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

    private static async Task<CreatureTemplate?> ResolveTemplateAsync(WorldSession session, uint entry, CancellationToken ct)
    {
        try
        {
            var t = await session.WorldDb.GetCreatureTemplateAsync(entry, ct);
            if (t is not null)
                return new CreatureTemplate(t.Entry, t.Name, t.SubName ?? string.Empty, t.DisplayId1,
                    t.MinLevel, t.Faction, t.CreatureType, t.Scale, t.NpcFlags, t.UnitClass);
        }
        catch (Exception ex)
        {
            session.Logger.LogDebug("CREATURE_QUERY {Entry}: БД мира недоступна ({Msg})", entry, ex.Message);
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
