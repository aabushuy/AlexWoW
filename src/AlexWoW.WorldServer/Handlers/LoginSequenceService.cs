using AlexWoW.Common.Network;
using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Models;
using AlexWoW.DataStores.Terrain;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Оркестрация полной последовательности входа в мир (M4; DI-сервис M7 S7 — вынос из легаси-статика
/// WorldEntryHandlers): verify world, account data, книга спеллов, панели действий, инвентарь, статы,
/// состояние квестов, навыки, self-спавн, time sync, таланты, ауры, окрестные NPC/GO/игроки.
/// Порядок шагов — как в эталонных ядрах (CMaNGOS/TrinityCore), иначе клиент не отдаёт управление.
/// Опкод-вход — <see cref="WorldEntryOpcodeHandlers"/>.
/// </summary>
internal sealed class LoginSequenceService(
    ICharacterRepository characters,
    IInventoryRepository items,
    IWorldRepository worldDb,
    ICharacterStateRepository charState,
    TerrainMaps terrain,
    CharScreenHandlers charScreen,
    ActionBarHandlers actionBars,
    TimeSyncService timeSync,
    World.VisibilityService visibility,
    QuestProgressService questProgress,
    AuraPersistenceService auraPersistence,
    StartingGearService startingGear,
    SkillsService skills,
    TalentHandlers talents,
    SpellModifierService spellMods,
    ProgressionService progression)
{
    /// <summary>Вход персонажа в мир по CMSG_PLAYER_LOGIN (валидация владения + вся последовательность).</summary>
    internal async Task LoginAsync(WorldSession session, uint guid, CancellationToken ct)
    {
        var character = await characters.GetByGuidAsync(guid, ct);
        if (character is null || character.AccountId != session.AccountId)
        {
            session.Logger.LogWarning("PLAYER_LOGIN: персонаж guid={Guid} не найден/чужой для '{User}'", guid, session.Account);
            return;
        }

        session.InWorldGuid = character.Guid;
        session.Character = character; // M7 #16: нужен в RefreshMeleeAsync (AP по статам) — ставим РАНО
        session.PosX = character.X;
        session.PosY = character.Y;
        session.PosZ = character.Z;

        // Полная последовательность входа (порядок как в эталонных ядрах) — иначе клиент не отдаёт управление.
        var verify = new ByteWriter(20)
            .UInt32(character.Map)
            .Single(character.X).Single(character.Y).Single(character.Z)
            .Single(0f);
        await session.SendAsync(WorldOpcode.SmsgLoginVerifyWorld, verify.ToArray(), ct);

        await charScreen.SendAccountDataTimesAsync(session, 0xEA, ct);

        await session.SendAsync(WorldOpcode.SmsgFeatureSystemStatus,
            new ByteWriter(2).UInt8(2).UInt8(0).ToArray(), ct);

        await SendInitialSpellsAsync(session, character, ct);
        await spellMods.RebuildAsync(session, ct); // M10.6: модификаторы пассивных талантов из KnownSpells
        await actionBars.SendInitialActionButtonsAsync(session, ct); // M7 #17: ярлыки панелей

        var tutorials = new ByteWriter(32);
        for (var i = 0; i < 8; i++)
            tutorials.UInt32(0);
        await session.SendAsync(WorldOpcode.SmsgTutorialFlags, tutorials.ToArray(), ct);

        await SendLoginTimeSpeedAsync(session, ct);
        await SendInitializeFactionsAsync(session, ct);

        // M6.1: инвентарь — выдать стартовый набор голым персонажам, загрузить и создать item-объекты
        // у клиента ДО спавна игрока (self-update ссылается на guid'ы предметов в слотах).
        if (!await items.HasItemsAsync(character.Guid, ct))
            await startingGear.GiveAsync(session, character.Guid, character.Race, character.Class, ct);
        session.Inv.Inventory.Clear();
        session.Inv.Inventory.AddRange(await items.GetItemsAsync(character.Guid, ct));
        session.Inv.Money = character.Money; // M6.2: деньги для торговли

        // M6.13: class/ContainerSlots/MaxDurability по entry'ям инвентаря (батч, кэш на сессии) — чтобы
        // предметы-сумки (class=1) создавались как TYPEID_CONTAINER (иначе клиент крашится, баг #31).
        session.Inv.ItemBagInfo.Clear();
        try
        {
            var entries = session.Inv.Inventory.Select(i => i.ItemEntry).Distinct().ToArray();
            foreach (var (entry, info) in await worldDb.GetItemBagInfoAsync(entries, ct))
                session.Inv.ItemBagInfo[entry] = info;
        }
        catch (Exception ex)
        {
            session.Logger.LogDebug(ex, "M6.13: bag-info недоступен ({Msg}) — сумки как обычные предметы", ex.Message);
        }

        // M9.2: статы/HP/мана по классу+уровню (player_levelstats); фолбэк — флэт. Полное HP при входе.
        await session.World.Stats.EnsureLoadedAsync(ct);
        var stats = session.World.Stats.Compute(character.Race, character.Class, character.Level);
        session.Combat.MaxHealth = stats?.MaxHealth ?? DisplayData.MaxHealthForLevel(character.Level);
        session.Combat.Health = session.Combat.MaxHealth;
        session.Combat.IsDead = false;
        session.Combat.LastCombatMs = 0;

        // M6.4: мана для каста (полный пул при входе). MaxMana=0 у rage/energy-классов — расход не применяется.
        session.Cast.MaxMana = stats?.MaxMana ?? DisplayData.MaxManaForClass(character.Class, character.Level);
        session.Cast.Mana = session.Cast.MaxMana;
        session.Progression.Xp = character.Xp; // M9.1: текущий опыт на уровне
        session.Cast.LastSpellCastMs = 0;
        session.Cast.LastManaRegenMs = Environment.TickCount64;
        session.Cast.SpellCooldowns.Clear();

        // M6.12: боевые ресурсы. Воин — ярость 0 (копится в бою); разбойник — энергия полная (регенит).
        session.Combat.Rage = 0;
        session.Combat.Energy = DisplayData.PowerTypeForClass(character.Class) == 3 ? 100u : 0u;
        session.Combat.LastResourceTickMs = Environment.TickCount64;
        if (session.Inv.Inventory.Count > 0)
        {
            await session.SendAsync(WorldOpcode.SmsgUpdateObject,
                ItemObject.BuildItemsCreate(session.Inv.Inventory, character.Guid, session.Inv.ItemBagInfo), ct);
        }

        // M6.10: восстановить состояние квестов ДО спавна — поля журнала кладутся в начальный спавн
        // (иначе досылка отдельным апдейтом = «новое взятие» со звуком при релоге).
        await questProgress.LoadQuestStateAsync(session, ct);

        // M11.1: навыки персонажа (профессии и пр.) — загрузить ДО спавна, чтобы лечь в слоты PLAYER_SKILL_INFO.
        await skills.LoadAsync(session, character.Guid, character.Race, ct);

        var spawn = PlayerSpawn.BuildCreateObject(character,
            character.X, character.Y, character.Z, 0f, (uint)Environment.TickCount, isSelf: true,
            session.Inv.Inventory, session.Quest.QuestSlots, stats, session.Progression.SkillBook.Skills);
        await session.SendAsync(WorldOpcode.SmsgUpdateObject, spawn, ct);

        // Без time sync игрок не управляется. Заодно — первая точка синхронизации часов (M6.3 ч.2).
        await timeSync.SendTimeSyncReqAsync(session, ct);

        // M9.7: загрузить изученные таланты (для панели + расчёта потраченных очков). Ранг-спеллы уже
        // в KnownSpells (character_spell). M9.6: свободные очки = MaxPoints − потрачено.
        session.Progression.LearnedTalents.Clear();
        foreach (var (tid, rank) in await charState.GetTalentsAsync(character.Guid, ct))
            session.Progression.LearnedTalents[tid] = rank;
        talents.RecomputePoints(session, character.Class, character.Level);

        // M9.1: XP-бар — текущий опыт + порог следующего уровня. M9.6: очки талантов в то же поле-апдейт.
        await session.World.Levels.EnsureLoadedAsync(ct);
        await session.SendAsync(WorldOpcode.SmsgUpdateObject,
            PlayerSpawn.BuildPlayerValuesUpdate((ulong)session.InWorldGuid, m =>
            {
                m.SetUInt32(UpdateField.PlayerXp, session.Progression.Xp);
                m.SetUInt32(UpdateField.PlayerNextLevelXp, session.World.Levels.XpToNext(character.Level));
                m.SetUInt32(UpdateField.PlayerCharacterPoints1, session.Progression.TalentPoints);
            }), ct);

        // M9.6: состояние талантов (открывает панель; деревья клиент рисует сам из своей DBC).
        await talents.SendTalentsInfoAsync(session, ct);

        // M9.2: боевые поля (урон/скорость) из экипированного оружия — чтобы чарпейн не показывал INF.
        await progression.RefreshMeleeAsync(session, ct);

        session.Logger.LogInformation("PLAYER_LOGIN '{Name}' (guid={Guid}) → мир: map={Map} ({X};{Y};{Z})",
            character.Name, guid, character.Map, character.X, character.Y, character.Z);

        // M5.5: проверка рельефа — высота земли в точке входа против сохранённой Z.
        var ground = terrain.GetHeight(character.Map, character.X, character.Y);
        if (ground is { } g)
        {
            session.Logger.LogInformation("Рельеф: земля в ({X};{Y}) = {Ground:F2} (Z персонажа {Z:F2}, дельта {Delta:F2})",
                character.X, character.Y, g, character.Z, character.Z - g);
        }

        // M5.1/M5.6: показать существ и гейм-объекты из БД мира вокруг (диф-видимость).
        await visibility.RefreshVisibleNpcsAsync(session, character.Map, character.X, character.Y, ct);
        await visibility.RefreshVisibleGameObjectsAsync(session, character.Map, character.X, character.Y, ct);

        // M5.3: зарегистрировать в мире и обоюдно спавнить с соседними игроками.
        session.Character = character;
        var player = new World.WorldPlayer { Guid = character.Guid, Character = character, Session = session };
        session.Player = player;
        await session.World.EnterWorldAsync(player, ct);

        // M7 #21: восстановить сохранённые переключатели (стойка воина/аура паладина/аспект охотника).
        await auraPersistence.ReapplyPersistedAsync(session, ct);

        // Клиент теряет экипировку соседей, если их create приходит во время загрузочного экрана.
        // Досылаем create соседей повторно, когда загрузка точно завершена (две попытки).
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(1500, ct);
                await session.World.ResendNearbyEquipmentToAsync(player, ct);
                await Task.Delay(2500, ct);
                await session.World.ResendNearbyEquipmentToAsync(player, ct);
            }
            catch (Exception ex)
            {
                session.Logger.LogDebug(ex, "Повторная досылка соседей '{User}': {Msg}", session.Account, ex.Message);
            }
        }, ct);
    }

    /// <summary>
    /// SMSG_INITIAL_SPELLS (M9.3): книга заклинаний при входе. Набор = языковые спеллы расы ∪ стартовые
    /// абилки класса (playercreateinfo_spell) ∪ изученное у тренера (character_spell). Заполняет
    /// session.Progression.KnownSpells (для HasSpell-проверок тренера). БД мира недоступна → фолбэк на языковые.
    /// </summary>
    private async Task SendInitialSpellsAsync(WorldSession session, Character character, CancellationToken ct)
    {
        var known = session.Progression.KnownSpells;
        known.Clear();
        foreach (var s in LanguageSpells.ForRace(character.Race))
            known.Add((uint)s);

        try
        {
            foreach (var s in await worldDb.GetStartSpellsAsync(character.Race, character.Class, ct))
                known.Add(s);
        }
        catch (Exception ex)
        {
            session.Logger.LogDebug(ex, "INITIAL_SPELLS '{User}': стартовые спеллы из БД недоступны ({Msg})",
                session.Account, ex.Message);
        }

        // Изученное у тренера (персист).
        foreach (var s in await charState.GetLearnedSpellsAsync(character.Guid, ct))
            known.Add(s);

        // M11 (#2/#3): причесать книгу профессий одним батч-запросом:
        //  • учителя (LEARN_SPELL, напр. «Подмастерье кузнеца») прячем; их изучаемый спелл-открывашку
        //    окна (2018) добавляем в книгу/персист, если не выучен (самолечение персонажей до фикса #2);
        //  • тир-дедуп: показываем только высший тир каждой профессии (низшие superceded).
        // Спрятанные остаются в KnownSpells (для каста/валидации), но в книгу не шлём.
        session.Progression.ProfessionRankSpell.Clear();
        var hiddenSpells = new HashSet<uint>();
        try
        {
            var templates = (await worldDb.GetSpellsAsync([.. known], ct)).ToList();
            foreach (var tpl in templates)
            {
                var taught = World.Professions.TaughtSpell(tpl);
                if (taught == 0)
                    continue;
                hiddenSpells.Add(tpl.Id);                 // учитель — не книжный спелл
                if (known.Add(taught))                    // изучаемый спелл отсутствовал → выучить
                    await charState.AddLearnedSpellAsync(character.Guid, taught, ct);
            }
            foreach (var tpl in templates)
            {
                if (hiddenSpells.Contains(tpl.Id) || World.Professions.SkillGrantedBy(tpl) is not { } g)
                    continue;
                if (session.Progression.ProfessionRankSpell.TryGetValue(g.SkillId, out var cur))
                {
                    hiddenSpells.Add(g.Max > cur.Max ? cur.Spell : tpl.Id);
                    if (g.Max > cur.Max)
                        session.Progression.ProfessionRankSpell[g.SkillId] = (tpl.Id, g.Max);
                }
                else
                {
                    session.Progression.ProfessionRankSpell[g.SkillId] = (tpl.Id, g.Max);
                }
            }
        }
        catch (Exception ex)
        {
            session.Logger.LogDebug(ex, "INITIAL_SPELLS '{User}': причёсывание профессий пропущено ({Msg})", session.Account, ex.Message);
        }

        var book = known.Where(s => !hiddenSpells.Contains(s)).ToList();
        var w = new ByteWriter(8 + (book.Count * 6))
            .UInt8(0)
            .UInt16((ushort)book.Count);
        foreach (var spell in book)
            w.UInt32(spell).UInt16(0); // 3.3.5: spellId — u32 + u16
        w.UInt16(0); // нет кулдаунов
        await session.SendAsync(WorldOpcode.SmsgInitialSpells, w.ToArray(), ct);
        session.Logger.LogDebug("INITIAL_SPELLS '{User}': {Count} спеллов (класс {Class})",
            session.Account, known.Count, character.Class);
    }

    /// <summary>
    /// SMSG_INITIALIZE_FACTIONS (0x122) — инициализирует менеджер репутаций клиента (M7 #11). Без него
    /// клиент не отдаёт корректную НЕЙТРАЛЬНУЮ реакцию для существ, чьи фракции не имеют явных
    /// hostile/friendly масок (напр. нейтральные мобы Элвинна) — и блокирует атаку по ним. Шлём 128
    /// слотов (Wrath) с нулевым флагом/standing'ом: персонаж на базовой репутации, список просто
    /// «существует». Точные standing'и/at-war по Faction.dbc — задел на полноценную репутацию (квест-награды).
    /// Структура (wow_messages, vers.3): u32 count + count×(u8 flag + u32 standing).
    /// </summary>
    private static async Task SendInitializeFactionsAsync(WorldSession session, CancellationToken ct)
    {
        const int FactionCount = 128; // mangostwo (wrath) = 0x80
        var w = new ByteWriter(4 + (FactionCount * 5));
        w.UInt32(FactionCount);
        for (var i = 0; i < FactionCount; i++)
            w.UInt8(0).UInt32(0); // flag=0 (невидима/не-at-war), standing=0 (база)
        await session.SendAsync(WorldOpcode.SmsgInitializeFactions, w.ToArray(), ct);
    }

    private static async Task SendLoginTimeSpeedAsync(WorldSession session, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var packed = (uint)(
            now.Minute
            | (now.Hour << 6)
            | ((int)now.DayOfWeek << 11)
            | ((now.Day - 1) << 14)
            | ((now.Month - 1) << 20)
            | ((now.Year - 2000) << 24));
        var w = new ByteWriter(12)
            .UInt32(packed)
            .Single(0.01666667f)
            .UInt32(0);
        await session.SendAsync(WorldOpcode.SmsgLoginSetTimeSpeed, w.ToArray(), ct);
    }
}
