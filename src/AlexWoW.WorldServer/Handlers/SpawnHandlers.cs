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
    /// <summary>Сколько существ максимум держим видимыми (защита от перегруза в городах).</summary>
    private const int MaxNearbyNpcs = 150;

    /// <summary>Дистанция (ярды), при превышении которой пересчитываем видимость NPC при движении.</summary>
    public const float VisRefreshStep = 20f;

    /// <summary>
    /// Пересчитывает видимый набор NPC для текущей позиции игрока: шлёт CREATE для новых,
    /// DESTROY для ушедших из зоны. Используется и на входе (набор пуст → все CREATE),
    /// и при движении (троттлинг — см. MovementHandlers).
    /// </summary>
    internal static async Task RefreshVisibleNpcsAsync(WorldSession session, uint map, float x, float y, CancellationToken ct)
    {
        session.LastVisX = x;
        session.LastVisY = y;

        IReadOnlyList<CreatureSpawnData> rows;
        try
        {
            rows = await session.WorldDb.GetCreaturesNearAsync(
                map, x, y, World.WorldState.VisibilityRange, MaxNearbyNpcs, ct);
        }
        catch (Exception ex)
        {
            // БД мира недоступна: при первом пересчёте покажем одного тестового NPC.
            if (session.VisibleNpcs.Count == 0)
            {
                session.Logger.LogWarning("БД мира недоступна ({Msg}) — показываю тестового NPC", ex.Message);
                await SendTestNpcAsync(session, x, y, session.PosZ, ct);
            }
            return;
        }

        var newSet = new Dictionary<ulong, World.WorldCreature>(rows.Count);
        foreach (var row in rows)
        {
            if (row.DisplayId == 0)
                continue; // нет модели — не отрисуется
            var guid = Npcs.UnitGuid(row.Entry, row.Guid);
            // M6.3: одна авторитетная сущность на GUID для всех наблюдателей (общее HP/смерть/респавн).
            var creature = session.World.GetOrAddCreature(guid, () =>
            {
                var template = new CreatureTemplate(
                    row.Entry, row.Name, row.SubName ?? string.Empty, row.DisplayId,
                    row.MinLevel, row.Faction, row.CreatureType, row.Scale, row.NpcFlags, row.UnitClass);
                var maxHealth = World.WorldCreature.MaxHealthFor(row.MinLevel);
                return new World.WorldCreature
                {
                    Guid = guid, Map = map, Template = template,
                    X = row.X, Y = row.Y, Z = row.Z, O = row.O,
                    HomeX = row.X, HomeY = row.Y, HomeZ = row.Z, HomeO = row.O,
                    MaxHealth = maxHealth, Health = maxHealth,
                };
            });
            newSet[guid] = creature;
        }

        var time = (uint)Environment.TickCount;

        // Ушедшие из зоны → DESTROY.
        var gone = session.VisibleNpcs.Keys.Where(g => !newSet.ContainsKey(g)).ToArray();
        foreach (var guid in gone)
        {
            await session.SendAsync(WorldOpcode.SmsgDestroyObject,
                new ByteWriter(9).UInt64(guid).UInt8(0).ToArray(), ct);
            session.VisibleNpcs.TryRemove(guid, out _);
        }

        // Новые в зоне → CREATE.
        var added = 0;
        foreach (var (guid, creature) in newSet)
        {
            if (session.VisibleNpcs.ContainsKey(guid))
                continue;
            await session.SendAsync(WorldOpcode.SmsgUpdateObject, CreatureUpdate.BuildCreateObject(creature, time), ct);
            session.VisibleNpcs[guid] = creature;
            added++;
        }

        if (added > 0 || gone.Length > 0)
            session.Logger.LogDebug("Видимость NPC '{User}': +{Added} -{Gone} (всего {Total})",
                session.Account, added, gone.Length, session.VisibleNpcs.Count);
    }

    /// <summary>
    /// Пересчёт видимых гейм-объектов для текущей позиции (диф: CREATE новых / DESTROY ушедших).
    /// Аналогично NPC, вызывается на входе и при движении (в той же троттлинг-точке).
    /// </summary>
    internal static async Task RefreshVisibleGameObjectsAsync(WorldSession session, uint map, float x, float y, CancellationToken ct)
    {
        IReadOnlyList<GameObjectSpawnData> rows;
        try
        {
            rows = await session.WorldDb.GetGameObjectsNearAsync(
                map, x, y, World.WorldState.VisibilityRange, MaxNearbyNpcs, ct);
        }
        catch
        {
            return; // БД мира недоступна — без гейм-объектов
        }

        var newSet = new Dictionary<ulong, GoSpawn>(rows.Count);
        foreach (var row in rows)
        {
            var guid = GameObjects.GameObjectGuid(row.Entry, row.Guid);
            var template = new GoTemplate(row.Entry, row.Type, row.DisplayId, row.Name, row.Faction, row.Flags, row.Size);
            newSet[guid] = new GoSpawn(guid, template, row.X, row.Y, row.Z, row.O,
                row.Rot0, row.Rot1, row.Rot2, row.Rot3);
        }

        var gone = session.VisibleGos.Keys.Where(g => !newSet.ContainsKey(g)).ToArray();
        foreach (var guid in gone)
        {
            await session.SendAsync(WorldOpcode.SmsgDestroyObject,
                new ByteWriter(9).UInt64(guid).UInt8(0).ToArray(), ct);
            session.VisibleGos.Remove(guid);
        }

        foreach (var (guid, go) in newSet)
        {
            if (session.VisibleGos.ContainsKey(guid))
                continue;
            await session.SendAsync(WorldOpcode.SmsgUpdateObject, GameObjectUpdate.BuildCreateObject(go), ct);
            session.VisibleGos[guid] = go;
        }
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgGameObjectQuery)]
    public static async Task OnGameObjectQuery(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var reader = packet.Reader();
        var entry = reader.UInt32();
        // далее u64 guid — для ответа не нужен.

        GameObjectTemplateData? t = null;
        try { t = await session.WorldDb.GetGameObjectTemplateAsync(entry, ct); }
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

    private static async Task SendTestNpcAsync(WorldSession session, float x, float y, float z, CancellationToken ct)
    {
        var guid = Npcs.UnitGuid(Npcs.TestDummy.Entry, counter: 1);
        var creature = session.World.GetOrAddCreature(guid, () =>
        {
            var maxHealth = World.WorldCreature.MaxHealthFor(Npcs.TestDummy.Level);
            return new World.WorldCreature
            {
                Guid = guid, Map = session.Character?.Map ?? 0, Template = Npcs.TestDummy,
                X = x + 4f, Y = y, Z = z, O = MathF.PI,
                HomeX = x + 4f, HomeY = y, HomeZ = z, HomeO = MathF.PI,
                MaxHealth = maxHealth, Health = maxHealth,
            };
        });
        session.VisibleNpcs[guid] = creature;
        await session.SendAsync(WorldOpcode.SmsgUpdateObject,
            CreatureUpdate.BuildCreateObject(creature, (uint)Environment.TickCount), ct);
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
