using AlexWoW.Common.Network;
using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Models;
using AlexWoW.DataStores.Navigation;
using AlexWoW.DataStores.Terrain;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.World;

/// <summary>
/// Авторитетный контроль существ (SRP-часть <see cref="WorldState"/>, рефактор #30): движение/фейсинг
/// по навмешу, респавн, тренировочный манекен дев-команды. Реестр/рассылку берёт из
/// <see cref="WorldState"/>; навмеш — для путей по земле.
/// </summary>
public sealed class CreatureDirector(WorldState world, Navmesh navmesh, IWorldRepository worldDb, TerrainMaps terrain, ILogger logger)
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
    public Task SummonTrainingDummyAsync(WorldSession session, CancellationToken ct)
        => SummonDummyAsync(session, Npcs.TrainingDummyGuid, Npcs.TrainingDummy, Npcs.TrainingDummyHealth,
            sideOffset: 0f, wounded: false, ct);

    /// <summary>
    /// Лечебный манекен (M12 Spell QA): дружественная цель для проверки хилов/HoT. Призывается ранен (HP = ½
    /// макс.) — лечащий спелл всегда даёт effective&gt;0. Ставится со смещением вбок, чтобы не накладываться на
    /// урон-манекен. Хил по нему обрабатывает <see cref="Handlers.SpellEffectsService.ApplyHealAsync"/>.
    /// </summary>
    public Task SummonHealDummyAsync(WorldSession session, CancellationToken ct)
        => SummonDummyAsync(session, Npcs.HealDummyGuid, Npcs.HealDummy, Npcs.HealDummyHealth,
            sideOffset: 2.5f, wounded: true, ct);

    /// <summary>Атакующий манекен (проверка защиты): уровень 80, отвечает при атаке — бьёт игрока, чтобы
    /// проверять уклонение/парирование/блок/броню/«Глухую оборону». Ставится сбоку, чтобы не слипаться.</summary>
    public Task SummonAttackDummyAsync(WorldSession session, CancellationToken ct)
        => SummonDummyAsync(session, Npcs.AttackDummyGuid, Npcs.AttackDummy, Npcs.AttackDummyHealth,
            sideOffset: -2.5f, wounded: false, ct);

    /// <summary>Кастующий манекен (Фаза 2 INT.1): крутит каст-бар по игроку — стенд для проверки прерывания.
    /// В бою с игроком (CombatTargetGuid), чтобы тикала AI; первый каст — почти сразу.</summary>
    public async Task SummonCasterDummyAsync(WorldSession session, CancellationToken ct)
    {
        await SummonDummyAsync(session, Npcs.CasterDummyGuid, Npcs.CasterDummy, Npcs.CasterDummyHealth,
            sideOffset: 5f, wounded: false, ct);
        if (world.FindCreature(Npcs.CasterDummyGuid) is { } caster)
        {
            caster.CombatTargetGuid = (ulong)session.InWorldGuid; // вводим в «бой» → тикает TickCreatureCombatAsync
            caster.CastingSpellId = 0;
            caster.SchoolLockUntilMs = 0;
            caster.SchoolLockMask = 0;
            caster.NextCastMs = Environment.TickCount64 + 1000; // первый каст через ~1с

            // DSP.2: вешаем снимаемый Magic-бафф (стенд для Purge/Spellsteal).
            caster.BuffSpellId = Npcs.CasterDummyBuffSpellId;
            caster.BuffDispelType = Npcs.CasterDummyBuffDispelType;
            caster.BuffSlot = CasterDummyBuffSlot;
            var level = (byte)(session.Character?.Level ?? 80);
            const byte flags = Protocol.AuraFlags.Effect1 | Protocol.AuraFlags.Positive | Protocol.AuraFlags.SelfCast;
            await world.BroadcastToObserversAsync(caster, WorldOpcode.SmsgAuraUpdate,
                Protocol.AuraPackets.BuildApplyByCaster(caster.Guid, caster.Guid, CasterDummyBuffSlot,
                    Npcs.CasterDummyBuffSpellId, flags, level, 1, 0), ct);
        }
    }

    /// <summary>Слот ауры-баффа кастующего манекена (отличный от CC-слота 40). DSP.2.</summary>
    private const byte CasterDummyBuffSlot = 41;

    /// <summary>
    /// Общая логика призыва манекена (#29, расширено M12): телепортирует существо с фикс. GUID на ~3 ярда перед
    /// игроком (с боковым сдвигом <paramref name="sideOffset"/>), лицом к нему; DESTROY+CREATE наблюдателям; сброс
    /// боевого состояния. <paramref name="wounded"/> — выставить HP в ½ макс. (лечебный манекен), иначе полный.
    /// </summary>
    private async Task SummonDummyAsync(WorldSession session, ulong guid, CreatureTemplate template,
        uint maxHealth, float sideOffset, bool wounded, CancellationToken ct)
    {
        var map = session.Character?.Map ?? 0;
        // 3 ярда вперёд + боковой сдвиг (перпендикуляр к направлению взгляда) — чтобы два манекена не слипались.
        float x = session.PosX + 3f * MathF.Cos(session.PosO) + sideOffset * MathF.Cos(session.PosO + MathF.PI / 2);
        float y = session.PosY + 3f * MathF.Sin(session.PosO) + sideOffset * MathF.Sin(session.PosO + MathF.PI / 2);
        // Z по рельефу в точке манекена (а не Z игрока): на склоне земля под манекеном выше/ниже игрока —
        // иначе манекен проваливается в текстуры или висит в воздухе. Фолбэк на Z игрока, если рельеф недоступен.
        float z = terrain.GetHeight(map, x, y) ?? session.PosZ;
        float o = session.PosO + MathF.PI;            // лицом к игроку

        var dummy = world.GetOrAddCreature(guid, () => new WorldCreature
        {
            Guid = guid,
            Map = map,
            Template = template,
            X = x,
            Y = y,
            Z = z,
            O = o,
            HomeX = x,
            HomeY = y,
            HomeZ = z,
            HomeO = o,
            MaxHealth = maxHealth,
            Health = maxHealth,
        });

        // Убрать со старого места у всех, кто его видит (включая вызвавшего — ниже пере-создадим).
        var destroy = new ByteWriter(9).UInt64(dummy.Guid).UInt8(0).ToArray();
        foreach (var observer in world.ObserversOf(dummy).ToList())
        {
            await observer.Session.SendAsync(WorldOpcode.SmsgDestroyObject, destroy, ct);
            observer.Session.Visibility.VisibleNpcs.TryRemove(dummy.Guid, out _);
        }

        // Переставить (X/Y/Z мутабельны; Home — init, и не нужен: манекен пассивен, не евейдит/респавнит)
        // + полный сброс боевого состояния (как свежий манекен). Лечебный — глубоко ранен (1% макс., заведомо ниже
        // потолка лечения ½ макс. в ApplyHealAsync), чтобы любой хил давал effective>0 в течение всей сессии.
        dummy.X = x; dummy.Y = y; dummy.Z = z; dummy.O = o;
        dummy.Health = wounded ? Math.Max(1, dummy.MaxHealth / 100) : dummy.MaxHealth;
        dummy.CombatTargetGuid = 0;
        dummy.Evading = false;
        dummy.RespawnAtMs = null;
        dummy.Lootable = false;
        dummy.Loot = null;

        // Показать вызвавшему на новом месте.
        session.Visibility.VisibleNpcs[dummy.Guid] = dummy;
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
        try { row = await worldDb.GetCreatureTemplateAsync(entry, ct); }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "DEV summon entry={Entry}: БД мира недоступна ({Msg})", entry, ex.Message);
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
            Guid = guid,
            Map = map,
            Template = template,
            X = x,
            Y = y,
            Z = z,
            O = o,
            HomeX = x,
            HomeY = y,
            HomeZ = z,
            HomeO = o,
            MaxHealth = hp,
            Health = hp,
        });

        session.Visibility.VisibleNpcs[guid] = creature;
        session.Visibility.DevNpcs[slot] = guid;
        await session.SendAsync(WorldOpcode.SmsgUpdateObject,
            CreatureUpdate.BuildCreateObject(creature, (uint)Environment.TickCount), ct);
        logger.LogDebug("DEV summon '{User}': slot={Slot} entry={Entry} guid={Guid}",
            session.Account, slot, entry, guid);
        return true;
    }

    /// <summary>
    /// Дев-команда <c>.spawnenemy</c>: спавнит <paramref name="count"/> ВРАЖДЕБНЫХ существ типа
    /// <paramref name="creatureType"/> уровня <paramref name="level"/> кольцом вокруг игрока. Фракция 14
    /// («Monster» — враждебна всем игрокам) → авто-агро и ответный бой работают штатно (CreatureCombatAI).
    /// Каждый регистрируется в DevNpcs уникальным слотом (липкость в видимости + снятие через <c>.devclean</c>).
    /// Возвращает число заспавненных (0 — типа нет в БД или БД недоступна).
    /// </summary>
    public async Task<int> SpawnEnemiesAsync(WorldSession session, byte creatureType, byte level, int count, CancellationToken ct)
    {
        count = Math.Clamp(count, 1, 20);
        IReadOnlyList<CreatureTemplateData> templates;
        try { templates = await worldDb.GetCreatureTemplatesByTypeAsync(creatureType, count, ct); }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "SPAWNENEMY type={Type}: БД мира недоступна ({Msg})", creatureType, ex.Message);
            return 0;
        }
        if (templates.Count == 0)
            return 0;

        const ushort HostileFaction = 14; // «Monster» — враждебна всем игрокам (красное имя, авто-агро)
        var map = session.Character?.Map ?? 0;
        var hp = WorldCreature.MaxHealthFor(level);
        for (var i = 0; i < count; i++)
        {
            var row = templates[i % templates.Count]; // меньше типов, чем нужно → циклим
            var angle = session.PosO + i * (2f * MathF.PI / count); // равномерно кольцом вокруг игрока
            float x = session.PosX + 5f * MathF.Cos(angle);
            float y = session.PosY + 5f * MathF.Sin(angle);
            float z = session.PosZ;
            float o = angle + MathF.PI; // лицом к центру (игроку)

            var template = new CreatureTemplate(row.Entry, row.Name, row.SubName ?? string.Empty, row.DisplayId1,
                level, HostileFaction, row.CreatureType, row.Scale <= 0 ? 1f : row.Scale, 0, row.UnitClass);
            var guid = Npcs.UnitGuid(row.Entry, world.NextDevSpawnCounter());
            var creature = world.GetOrAddCreature(guid, () => new WorldCreature
            {
                Guid = guid,
                Map = map,
                Template = template,
                X = x,
                Y = y,
                Z = z,
                O = o,
                HomeX = x,
                HomeY = y,
                HomeZ = z,
                HomeO = o,
                MaxHealth = hp,
                Health = hp,
            });
            session.Visibility.VisibleNpcs[guid] = creature;
            session.Visibility.DevNpcs[$"enemy:{guid}"] = guid; // уникальный слот → снимается .devclean
            await session.SendAsync(WorldOpcode.SmsgUpdateObject,
                CreatureUpdate.BuildCreateObject(creature, (uint)Environment.TickCount), ct);
        }
        logger.LogDebug("SPAWNENEMY '{User}': type={Type} level={Level} count={Count}",
            session.Account, creatureType, level, count);
        return count;
    }

    /// <summary>Снимает dev-сущность из слота <paramref name="slot"/> (DESTROY вызвавшему + чистка реестров).
    /// Возвращает true, если что-то было снято. D1.</summary>
    public async Task<bool> DespawnDevNpcAsync(WorldSession session, string slot, CancellationToken ct)
    {
        if (!session.Visibility.DevNpcs.Remove(slot, out var guid))
            return false;
        await session.SendAsync(WorldOpcode.SmsgDestroyObject,
            new ByteWriter(9).UInt64(guid).UInt8(0).ToArray(), ct);
        session.Visibility.VisibleNpcs.TryRemove(guid, out _);
        world.RemoveCreature(guid);
        return true;
    }

    /// <summary>
    /// Дев-команда <c>.craft</c> (D3): спавнит гейм-объект <paramref name="entry"/> (крафт-станок/почта) у
    /// игрока. Прямая посылка SMSG_UPDATE_OBJECT — НЕ кладём в VisibleGos, поэтому пересчёт видимости его не
    /// трогает (липкость). Регистрируется в per-session реестре dev-GO по слоту <paramref name="slot"/>
    /// (replace). Возвращает false, если шаблон GO не найден. D3.
    /// </summary>
    public async Task<bool> SummonDevGoAsync(WorldSession session, uint entry, string slot, CancellationToken ct)
    {
        Database.Models.GameObjectTemplateData? t;
        try { t = await worldDb.GetGameObjectTemplateAsync(entry, ct); }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "DEV craft entry={Entry}: БД мира недоступна ({Msg})", entry, ex.Message);
            return false;
        }
        if (t is null)
            return false;

        await DespawnDevGoAsync(session, slot, ct);

        float x = session.PosX + 2f * MathF.Cos(session.PosO);
        float y = session.PosY + 2f * MathF.Sin(session.PosO);
        float z = session.PosZ;
        float o = session.PosO + MathF.PI;             // лицом к игроку
        // Поворот вокруг Z на угол o: кватернион (0,0,sin(o/2),cos(o/2)).
        float rz = MathF.Sin(o / 2f), rw = MathF.Cos(o / 2f);

        var guid = GameObjects.GameObjectGuid(entry, world.NextDevSpawnCounter());
        var template = new GoTemplate(t.Entry, t.Type, t.DisplayId, t.Name, 0, 0, t.Size <= 0 ? 1f : t.Size);
        var go = new GoSpawn(guid, template, x, y, z, o, 0f, 0f, rz, rw);

        session.Visibility.DevGos[slot] = guid;
        await session.SendAsync(WorldOpcode.SmsgUpdateObject, GameObjectUpdate.BuildCreateObject(go), ct);
        logger.LogDebug("DEV craft '{User}': slot={Slot} entry={Entry} guid={Guid}",
            session.Account, slot, entry, guid);
        return true;
    }

    /// <summary>Снимает dev-GO из слота <paramref name="slot"/> (DESTROY вызвавшему). true, если что-то снято. D3.</summary>
    public async Task<bool> DespawnDevGoAsync(WorldSession session, string slot, CancellationToken ct)
    {
        if (!session.Visibility.DevGos.Remove(slot, out var guid))
            return false;
        await session.SendAsync(WorldOpcode.SmsgDestroyObject,
            new ByteWriter(9).UInt64(guid).UInt8(0).ToArray(), ct);
        return true;
    }

    /// <summary>Снимает все dev-станки игрока (<c>.craft off</c>). D3.</summary>
    public async Task DevCleanGosAsync(WorldSession session, CancellationToken ct)
    {
        foreach (var slot in session.Visibility.DevGos.Keys.ToList())
            await DespawnDevGoAsync(session, slot, ct);
    }

    /// <summary>Снимает ВСЕ dev-сущности игрока — существа и станки (<c>.devclean</c>). Манекен <c>.dummy</c>
    /// не затронут. D1/D3.</summary>
    public async Task DevCleanAsync(WorldSession session, CancellationToken ct)
    {
        foreach (var slot in session.Visibility.DevNpcs.Keys.ToList())
            await DespawnDevNpcAsync(session, slot, ct);
        await DevCleanGosAsync(session, ct);
    }
}
