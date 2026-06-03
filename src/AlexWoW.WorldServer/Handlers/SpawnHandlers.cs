using AlexWoW.Common.Network;
using AlexWoW.Database.Models;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Видимость и существа (M5): спавн NPC у клиента (SMSG_UPDATE_OBJECT) и ответ на
/// CMSG_CREATURE_QUERY (имя/тип существа). Пока один захардкоженный тестовый NPC.
/// </summary>
public static class SpawnHandlers
{
    /// <summary>Спавнит тестового NPC рядом с игроком после его входа в мир.</summary>
    internal static async Task SendNearbyNpcsAsync(WorldSession session, Character character, CancellationToken ct)
    {
        // Ставим NPC в 4 ярдах «перед» персонажем, на ту же высоту (рельеф нам пока неизвестен — M5.5).
        var spawn = new NpcSpawn(
            Guid: Npcs.UnitGuid(Npcs.TestDummy.Entry, counter: 1),
            Template: Npcs.TestDummy,
            X: character.X + 4f,
            Y: character.Y,
            Z: character.Z,
            O: MathF.PI); // лицом к -X (примерно к игроку)

        session.VisibleNpcs[spawn.Guid] = spawn;

        var update = CreatureUpdate.BuildCreateObject(spawn, (uint)Environment.TickCount);
        await session.SendAsync(WorldOpcode.SmsgUpdateObject, update, ct);

        session.Logger.LogInformation("Спавн NPC '{Name}' (entry={Entry}, guid=0x{Guid:X}) у '{User}'",
            spawn.Template.Name, spawn.Template.Entry, spawn.Guid, session.Account);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgCreatureQuery)]
    public static async Task OnCreatureQuery(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var reader = packet.Reader();
        var entry = reader.UInt32();
        // далее u64 guid — для ответа не нужен.

        var template = Npcs.Find(entry);
        if (template is null)
        {
            // Нет данных: вернуть entry | 0x80000000 (клиент поймёт «шаблон неизвестен»).
            await session.SendAsync(WorldOpcode.SmsgCreatureQueryResponse,
                new ByteWriter(4).UInt32(entry | 0x80000000u).ToArray(), ct);
            return;
        }

        await session.SendAsync(WorldOpcode.SmsgCreatureQueryResponse, BuildQueryResponse(template), ct);
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
         .UInt32(t.UnitType)           // creature_type (7 = Humanoid)
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
