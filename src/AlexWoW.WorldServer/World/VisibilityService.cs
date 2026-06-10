using AlexWoW.Common.Network;
using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Models;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.World;

/// <summary>
/// Видимость окрестностей (M5/M5.6, DI-сервис M7 S7 — вынос из легаси-статика SpawnHandlers):
/// диф-пересчёт видимых NPC и гейм-объектов для текущей позиции игрока (CREATE новых / DESTROY ушедших).
/// Зовётся при входе в мир (<see cref="Handlers.LoginSequenceService"/>), при движении
/// (<see cref="Handlers.MovementHandlers"/>, троттлинг по дистанции) и при телепорте
/// (<see cref="TeleportService"/>). Источник — БД мира CMaNGOS; при её недоступности —
/// fallback на один захардкоженный тестовый NPC.
/// </summary>
internal sealed class VisibilityService(IWorldRepository worldDb)
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
    internal async Task RefreshVisibleNpcsAsync(WorldSession session, uint map, float x, float y, CancellationToken ct)
    {
        session.Visibility.LastVisX = x;
        session.Visibility.LastVisY = y;

        IReadOnlyList<CreatureSpawnData> rows;
        try
        {
            rows = await worldDb.GetCreaturesNearAsync(
                map, x, y, WorldState.VisibilityRange, MaxNearbyNpcs, ct);
        }
        catch (Exception ex)
        {
            // БД мира недоступна: при первом пересчёте покажем одного тестового NPC.
            if (session.Visibility.VisibleNpcs.Count == 0)
            {
                session.Logger.LogWarning("БД мира недоступна ({Msg}) — показываю тестового NPC", ex.Message);
                await SendTestNpcAsync(session, x, y, session.PosZ, ct);
            }
            return;
        }

        var newSet = new Dictionary<ulong, WorldCreature>(rows.Count);
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
                // Манекен (#28) — большой фиксированный HP, чтобы переживал тесты; остальные — по уровню.
                var maxHealth = Npcs.IsTrainingDummy(row.Entry)
                    ? Npcs.TrainingDummyHealth
                    : WorldCreature.MaxHealthFor(row.MinLevel);
                return new WorldCreature
                {
                    Guid = guid,
                    Map = map,
                    Template = template,
                    X = row.X,
                    Y = row.Y,
                    Z = row.Z,
                    O = row.O,
                    HomeX = row.X,
                    HomeY = row.Y,
                    HomeZ = row.Z,
                    HomeO = row.O,
                    MaxHealth = maxHealth,
                    Health = maxHealth,
                };
            });
            newSet[guid] = creature;
        }

        var time = (uint)Environment.TickCount;

        // Ушедшие из зоны → DESTROY. Dev-сущности (.trainer и т.п.) «липкие» — не из БД, при пересчёте
        // их нет в newSet; не сносим, иначе пропадали бы при ходьбе (D1).
        var gone = session.Visibility.VisibleNpcs.Keys.Where(g => !newSet.ContainsKey(g) && !session.Visibility.IsDevNpc(g)).ToArray();
        foreach (var guid in gone)
        {
            await session.SendAsync(WorldOpcode.SmsgDestroyObject,
                new ByteWriter(9).UInt64(guid).UInt8(0).ToArray(), ct);
            session.Visibility.VisibleNpcs.TryRemove(guid, out _);
        }

        // Новые в зоне → CREATE.
        var added = 0;
        foreach (var (guid, creature) in newSet)
        {
            if (session.Visibility.VisibleNpcs.ContainsKey(guid))
                continue;
            await session.SendAsync(WorldOpcode.SmsgUpdateObject, CreatureUpdate.BuildCreateObject(creature, time), ct);
            session.Visibility.VisibleNpcs[guid] = creature;
            added++;
        }

        if (added > 0 || gone.Length > 0)
            session.Logger.LogDebug("Видимость NPC '{User}': +{Added} -{Gone} (всего {Total})",
                session.Account, added, gone.Length, session.Visibility.VisibleNpcs.Count);
    }

    /// <summary>
    /// Пересчёт видимых гейм-объектов для текущей позиции (диф: CREATE новых / DESTROY ушедших).
    /// Аналогично NPC, вызывается на входе и при движении (в той же троттлинг-точке).
    /// </summary>
    internal async Task RefreshVisibleGameObjectsAsync(WorldSession session, uint map, float x, float y, CancellationToken ct)
    {
        IReadOnlyList<GameObjectSpawnData> rows;
        try
        {
            rows = await worldDb.GetGameObjectsNearAsync(
                map, x, y, WorldState.VisibilityRange, MaxNearbyNpcs, ct);
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

        var gone = session.Visibility.VisibleGos.Keys.Where(g => !newSet.ContainsKey(g)).ToArray();
        foreach (var guid in gone)
        {
            await session.SendAsync(WorldOpcode.SmsgDestroyObject,
                new ByteWriter(9).UInt64(guid).UInt8(0).ToArray(), ct);
            session.Visibility.VisibleGos.Remove(guid);
        }

        foreach (var (guid, go) in newSet)
        {
            if (session.Visibility.VisibleGos.ContainsKey(guid))
                continue;
            await session.SendAsync(WorldOpcode.SmsgUpdateObject, GameObjectUpdate.BuildCreateObject(go), ct);
            session.Visibility.VisibleGos[guid] = go;
        }
    }

    private static async Task SendTestNpcAsync(WorldSession session, float x, float y, float z, CancellationToken ct)
    {
        var guid = Npcs.UnitGuid(Npcs.TestDummy.Entry, counter: 1);
        var creature = session.World.GetOrAddCreature(guid, () =>
        {
            var maxHealth = WorldCreature.MaxHealthFor(Npcs.TestDummy.Level);
            return new WorldCreature
            {
                Guid = guid,
                Map = session.Character?.Map ?? 0,
                Template = Npcs.TestDummy,
                X = x + 4f,
                Y = y,
                Z = z,
                O = MathF.PI,
                HomeX = x + 4f,
                HomeY = y,
                HomeZ = z,
                HomeO = MathF.PI,
                MaxHealth = maxHealth,
                Health = maxHealth,
            };
        });
        session.Visibility.VisibleNpcs[guid] = creature;
        await session.SendAsync(WorldOpcode.SmsgUpdateObject,
            CreatureUpdate.BuildCreateObject(creature, (uint)Environment.TickCount), ct);
    }
}
