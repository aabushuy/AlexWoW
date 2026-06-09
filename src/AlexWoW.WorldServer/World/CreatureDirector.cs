using AlexWoW.Common.Network;
using AlexWoW.Database.Models;
using AlexWoW.DataStores.Navigation;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.World;

/// <summary>
/// Авторитетный контроль существ (SRP-часть <see cref="WorldState"/>, рефактор #30): движение/фейсинг
/// по навмешу, респавн, тренировочный манекен дев-команды. Реестр/рассылку берёт из
/// <see cref="WorldState"/>; навмеш — для путей по земле.
/// </summary>
public sealed class CreatureDirector(WorldState world, Navmesh navmesh, ILogger logger)
{
    /// <summary>Счётчик id сплайнов SMSG_MONSTER_MOVE (монотонный). M6.7.</summary>
    private int _splineId;

    /// <summary>
    /// Двигает существо в точку (M6.7): шлёт наблюдателям SMSG_MONSTER_MOVE (сплайн из текущей позиции),
    /// затем обновляет авторитетную позицию и фейсинг. <paramref name="durationMs"/> — время анимации хода.
    /// </summary>
    public async Task MoveCreatureAsync(WorldCreature creature, float nx, float ny, float nz, uint durationMs, CancellationToken ct)
    {
        float sx = creature.X, sy = creature.Y, sz = creature.Z;
        var splineId = (uint)System.Threading.Interlocked.Increment(ref _splineId);
        await world.BroadcastToObserversAsync(creature, WorldOpcode.SmsgMonsterMove,
            MonsterMove.Build(creature.Guid, sx, sy, sz, nx, ny, nz, durationMs, splineId), ct);

        creature.X = nx;
        creature.Y = ny;
        creature.Z = nz;
        float dx = nx - sx, dy = ny - sy;
        if (dx * dx + dy * dy > 1e-6f)
            creature.O = MathF.Atan2(dy, dx);
    }

    /// <summary>Доворот существа лицом к цели (без перемещения) — для страфа игрока в мили. M6.7.</summary>
    public async Task FaceCreatureAsync(WorldCreature creature, ulong targetGuid, CancellationToken ct)
    {
        var splineId = (uint)System.Threading.Interlocked.Increment(ref _splineId);
        await world.BroadcastToObserversAsync(creature, WorldOpcode.SmsgMonsterMove,
            MonsterMove.BuildFaceTarget(creature.Guid, creature.X, creature.Y, creature.Z, targetGuid, splineId), ct);
    }

    /// <summary>Путь по навмешу (mmaps) в игровых координатах или null (нет навмеша/пути). M6.7.</summary>
    public IReadOnlyList<(float X, float Y, float Z)>? FindGroundPath(uint map,
        float sx, float sy, float sz, float ex, float ey, float ez)
        => navmesh.FindPath(map, sx, sy, sz, ex, ey, ez);

    /// <summary>Воскрешает существо (полное HP) и шлёт наблюдателям апдейт здоровья. M6.3.</summary>
    public async Task RespawnCreatureAsync(WorldCreature creature, CancellationToken ct)
    {
        creature.Health = creature.MaxHealth;
        creature.RespawnAtMs = null;
        creature.CombatTargetGuid = 0;
        creature.Evading = false;       // M6.7: вернуть на спавн при респавне
        creature.X = creature.HomeX;
        creature.Y = creature.HomeY;
        creature.Z = creature.HomeZ;
        creature.O = creature.HomeO;
        creature.Lootable = false; // M6.6: труп больше не lootable
        creature.Loot = null;

        // M7 #15: пере-создать у наблюдателей на ТОЧКЕ СПАВНА (DESTROY+CREATE). Иначе оживший виден на
        // месте смерти (после погони — далеко от дома): мы сбрасываем позицию на Home, но клиенту об
        // этом не сообщали. CREATE несёт полное состояние (позиция/HP/флаги), снимая и труп, и lootable.
        var time = (uint)Environment.TickCount;
        var destroy = new ByteWriter(9).UInt64(creature.Guid).UInt8(0).ToArray();
        foreach (var observer in world.ObserversOf(creature).ToList())
        {
            await observer.Session.SendAsync(WorldOpcode.SmsgDestroyObject, destroy, ct);
            await observer.Session.SendAsync(WorldOpcode.SmsgUpdateObject,
                CreatureUpdate.BuildCreateObject(creature, time), ct);
        }
        logger.LogDebug("Существо '{Name}' (guid={Guid}) респавнилось на спавне", creature.Template.Name, creature.Guid);
    }

    /// <summary>
    /// Дев-команда <c>.dummy</c> (#29): телепортирует тренировочный манекен (тот же GUID, что у статичного
    /// БД-спавна в Нортшире) на ~3 ярда перед игроком, лицом к нему. DESTROY+CREATE наблюдателям; HP и
    /// боевое состояние сбрасываются. В пределах видимости БД-точки (вся Долина Североземья) остаётся;
    /// дальше может пропасть при обновлении видимости — тогда позвать заново.
    /// </summary>
    public async Task SummonTrainingDummyAsync(WorldSession session, CancellationToken ct)
    {
        var map = session.Character?.Map ?? 0;
        float x = session.PosX + 3f * MathF.Cos(session.PosO);
        float y = session.PosY + 3f * MathF.Sin(session.PosO);
        float z = session.PosZ;
        float o = session.PosO + MathF.PI;            // лицом к игроку

        var dummy = world.GetOrAddCreature(Npcs.TrainingDummyGuid, () =>
        {
            var hp = Npcs.TrainingDummyHealth;
            return new WorldCreature
            {
                Guid = Npcs.TrainingDummyGuid, Map = map, Template = Npcs.TrainingDummy,
                X = x, Y = y, Z = z, O = o, HomeX = x, HomeY = y, HomeZ = z, HomeO = o,
                MaxHealth = hp, Health = hp,
            };
        });

        // Убрать со старого места у всех, кто его видит (включая вызвавшего — ниже пере-создадим).
        var destroy = new ByteWriter(9).UInt64(dummy.Guid).UInt8(0).ToArray();
        foreach (var observer in world.ObserversOf(dummy).ToList())
        {
            await observer.Session.SendAsync(WorldOpcode.SmsgDestroyObject, destroy, ct);
            observer.Session.VisibleNpcs.TryRemove(dummy.Guid, out _);
        }

        // Переставить (X/Y/Z мутабельны; Home — init, и не нужен: манекен пассивен, не евейдит/респавнит)
        // + полный сброс боевого состояния (как свежий манекен).
        dummy.X = x; dummy.Y = y; dummy.Z = z; dummy.O = o;
        dummy.Health = dummy.MaxHealth;
        dummy.CombatTargetGuid = 0;
        dummy.Evading = false;
        dummy.RespawnAtMs = null;
        dummy.Lootable = false;
        dummy.Loot = null;

        // Показать вызвавшему на новом месте.
        session.VisibleNpcs[dummy.Guid] = dummy;
        await session.SendAsync(WorldOpcode.SmsgUpdateObject,
            CreatureUpdate.BuildCreateObject(dummy, (uint)Environment.TickCount), ct);
    }

    /// <summary>
    /// Дев-команда (D1, каркас): спавнит существо <paramref name="entry"/> у игрока, лицом к нему, и
    /// показывает только вызвавшему (in-memory, не из БД). GUID кодирует entry → TrainerHandlers/VendorHandlers
    /// резолвят тип по нему (госсип/список/покупка работают «бесплатно»). Регистрируется в per-session реестре
    /// dev-сущностей по слоту <paramref name="slot"/> (replace: повтор в тот же слот заменяет старую) — реестр
    /// делает сущность «липкой» в видимости (не сносится при ходьбе) и снимаемой через <c>.devclean</c>.
    /// Возвращает false, если шаблон существа не найден в БД мира. D1.
    /// </summary>
    public async Task<bool> SummonDevNpcAsync(WorldSession session, uint entry, string slot, CancellationToken ct)
    {
        CreatureTemplateData? row;
        try { row = await session.WorldDb.GetCreatureTemplateAsync(entry, ct); }
        catch (Exception ex)
        {
            logger.LogDebug("DEV summon entry={Entry}: БД мира недоступна ({Msg})", entry, ex.Message);
            return false;
        }
        if (row is null)
            return false;

        // Faction → 35 («дружелюбен ко ВСЕМ»): dev-сущность не должна быть враждебной игроку любой
        // фракции/расы (иначе госсип/торговля не откроются — ПКМ атакует). Так можно брать самого полного
        // тренера профессии (Grand Master), даже если в дампе он фракционный. D1/D2.
        const uint DevFriendlyFaction = 35;
        var template = new CreatureTemplate(
            row.Entry, row.Name, row.SubName ?? string.Empty, row.DisplayId1,
            row.MinLevel, DevFriendlyFaction, row.CreatureType, row.Scale, row.NpcFlags, row.UnitClass);

        // Заменить прежнюю сущность в этом слоте (только 1 тренер/вендор и т.п.).
        await DespawnDevNpcAsync(session, slot, ct);

        var map = session.Character?.Map ?? 0;
        float x = session.PosX + 3f * MathF.Cos(session.PosO);
        float y = session.PosY + 3f * MathF.Sin(session.PosO);
        float z = session.PosZ;
        float o = session.PosO + MathF.PI;             // лицом к игроку

        var guid = Npcs.UnitGuid(entry, world.NextDevSpawnCounter());
        var hp = WorldCreature.MaxHealthFor(template.Level);
        var creature = world.GetOrAddCreature(guid, () => new WorldCreature
        {
            Guid = guid, Map = map, Template = template,
            X = x, Y = y, Z = z, O = o, HomeX = x, HomeY = y, HomeZ = z, HomeO = o,
            MaxHealth = hp, Health = hp,
        });

        session.VisibleNpcs[guid] = creature;
        session.DevNpcs[slot] = guid;
        await session.SendAsync(WorldOpcode.SmsgUpdateObject,
            CreatureUpdate.BuildCreateObject(creature, (uint)Environment.TickCount), ct);
        logger.LogDebug("DEV summon '{User}': slot={Slot} entry={Entry} guid={Guid}",
            session.Account, slot, entry, guid);
        return true;
    }

    /// <summary>Снимает dev-сущность из слота <paramref name="slot"/> (DESTROY вызвавшему + чистка реестров).
    /// Возвращает true, если что-то было снято. D1.</summary>
    public async Task<bool> DespawnDevNpcAsync(WorldSession session, string slot, CancellationToken ct)
    {
        if (!session.DevNpcs.Remove(slot, out var guid))
            return false;
        await session.SendAsync(WorldOpcode.SmsgDestroyObject,
            new ByteWriter(9).UInt64(guid).UInt8(0).ToArray(), ct);
        session.VisibleNpcs.TryRemove(guid, out _);
        world.RemoveCreature(guid);
        return true;
    }

    /// <summary>Снимает ВСЕ dev-сущности игрока (<c>.devclean</c>). Манекен <c>.dummy</c> не затронут. D1.</summary>
    public async Task DevCleanAsync(WorldSession session, CancellationToken ct)
    {
        foreach (var slot in session.DevNpcs.Keys.ToList())
            await DespawnDevNpcAsync(session, slot, ct);
    }
}
