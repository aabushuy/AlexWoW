using AlexWoW.Common.Network;
using AlexWoW.Database.Models;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>Вход в мир (M4): player login + полная последовательность входа, логаут, time sync.</summary>
public static class WorldEntryHandlers
{
    [WorldOpcodeHandler(WorldOpcode.CmsgPlayerLogin)]
    public static async Task OnPlayerLogin(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var reader = packet.Reader();
        var guid = (uint)reader.UInt64();

        var character = await session.Characters.GetByGuidAsync(guid, ct);
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

        await CharScreenHandlers.SendAccountDataTimesAsync(session, 0xEA, ct);

        await session.SendAsync(WorldOpcode.SmsgFeatureSystemStatus,
            new ByteWriter(2).UInt8(2).UInt8(0).ToArray(), ct);

        await SendInitialSpellsAsync(session, character, ct);
        await ActionBarHandlers.SendInitialActionButtonsAsync(session, ct); // M7 #17: ярлыки панелей

        var tutorials = new ByteWriter(32);
        for (var i = 0; i < 8; i++)
            tutorials.UInt32(0);
        await session.SendAsync(WorldOpcode.SmsgTutorialFlags, tutorials.ToArray(), ct);

        await SendLoginTimeSpeedAsync(session, ct);
        await SendInitializeFactionsAsync(session, ct);

        // M6.1: инвентарь — выдать стартовый набор голым персонажам, загрузить и создать item-объекты
        // у клиента ДО спавна игрока (self-update ссылается на guid'ы предметов в слотах).
        if (!await session.Items.HasItemsAsync(character.Guid, ct))
            await StartingGear.GiveAsync(session, character.Guid, character.Race, character.Class, ct);
        session.Inventory.Clear();
        session.Inventory.AddRange(await session.Items.GetItemsAsync(character.Guid, ct));
        session.Money = character.Money; // M6.2: деньги для торговли

        // M6.13: class/ContainerSlots/MaxDurability по entry'ям инвентаря (батч, кэш на сессии) — чтобы
        // предметы-сумки (class=1) создавались как TYPEID_CONTAINER (иначе клиент крашится, баг #31).
        session.ItemBagInfo.Clear();
        try
        {
            var entries = session.Inventory.Select(i => i.ItemEntry).Distinct().ToArray();
            foreach (var (entry, info) in await session.WorldDb.GetItemBagInfoAsync(entries, ct))
                session.ItemBagInfo[entry] = info;
        }
        catch (Exception ex)
        {
            session.Logger.LogDebug("M6.13: bag-info недоступен ({Msg}) — сумки как обычные предметы", ex.Message);
        }

        // M9.2: статы/HP/мана по классу+уровню (player_levelstats); фолбэк — флэт. Полное HP при входе.
        await session.World.Stats.EnsureLoadedAsync(ct);
        var stats = session.World.Stats.Compute(character.Race, character.Class, character.Level);
        session.MaxHealth = stats?.MaxHealth ?? DisplayData.MaxHealthForLevel(character.Level);
        session.Health = session.MaxHealth;
        session.IsDead = false;
        session.LastCombatMs = 0;

        // M6.4: мана для каста (полный пул при входе). MaxMana=0 у rage/energy-классов — расход не применяется.
        session.MaxMana = stats?.MaxMana ?? DisplayData.MaxManaForClass(character.Class, character.Level);
        session.Mana = session.MaxMana;
        session.Xp = character.Xp; // M9.1: текущий опыт на уровне
        session.LastSpellCastMs = 0;
        session.LastManaRegenMs = Environment.TickCount64;
        session.SpellCooldowns.Clear();

        // M6.12: боевые ресурсы. Воин — ярость 0 (копится в бою); разбойник — энергия полная (регенит).
        session.Rage = 0;
        session.Energy = DisplayData.PowerTypeForClass(character.Class) == 3 ? 100u : 0u;
        session.LastResourceTickMs = Environment.TickCount64;
        if (session.Inventory.Count > 0)
            await session.SendAsync(WorldOpcode.SmsgUpdateObject,
                ItemObject.BuildItemsCreate(session.Inventory, character.Guid, session.ItemBagInfo), ct);

        // M6.10: восстановить состояние квестов ДО спавна — поля журнала кладутся в начальный спавн
        // (иначе досылка отдельным апдейтом = «новое взятие» со звуком при релоге).
        await QuestHandlers.LoadQuestStateAsync(session, ct);

        // M11.1: навыки персонажа (профессии и пр.) — загрузить ДО спавна, чтобы лечь в слоты PLAYER_SKILL_INFO.
        await Skills.LoadAsync(session, character.Guid, character.Race, ct);

        var spawn = PlayerSpawn.BuildCreateObject(character,
            character.X, character.Y, character.Z, 0f, (uint)Environment.TickCount, isSelf: true,
            session.Inventory, session.QuestSlots, stats, session.SkillBook.Skills);
        await session.SendAsync(WorldOpcode.SmsgUpdateObject, spawn, ct);

        // Без time sync игрок не управляется. Заодно — первая точка синхронизации часов (M6.3 ч.2).
        await SendTimeSyncReqAsync(session, ct);

        // M9.7: загрузить изученные таланты (для панели + расчёта потраченных очков). Ранг-спеллы уже
        // в KnownSpells (character_spell). M9.6: свободные очки = MaxPoints − потрачено.
        session.LearnedTalents.Clear();
        foreach (var (tid, rank) in await session.CharState.GetTalentsAsync(character.Guid, ct))
            session.LearnedTalents[tid] = rank;
        TalentHandlers.RecomputePoints(session, character.Class, character.Level);

        // M9.1: XP-бар — текущий опыт + порог следующего уровня. M9.6: очки талантов в то же поле-апдейт.
        await session.World.Levels.EnsureLoadedAsync(ct);
        await session.SendAsync(WorldOpcode.SmsgUpdateObject,
            PlayerSpawn.BuildPlayerValuesUpdate((ulong)session.InWorldGuid, m =>
            {
                m.SetUInt32(UpdateField.PlayerXp, session.Xp);
                m.SetUInt32(UpdateField.PlayerNextLevelXp, session.World.Levels.XpToNext(character.Level));
                m.SetUInt32(UpdateField.PlayerCharacterPoints1, session.TalentPoints);
            }), ct);

        // M9.6: состояние талантов (открывает панель; деревья клиент рисует сам из своей DBC).
        await TalentHandlers.SendTalentsInfoAsync(session, ct);

        // M9.2: боевые поля (урон/скорость) из экипированного оружия — чтобы чарпейн не показывал INF.
        await Progression.RefreshMeleeAsync(session, ct);

        session.Logger.LogInformation("PLAYER_LOGIN '{Name}' (guid={Guid}) → мир: map={Map} ({X};{Y};{Z})",
            character.Name, guid, character.Map, character.X, character.Y, character.Z);

        // M5.5: проверка рельефа — высота земли в точке входа против сохранённой Z.
        var ground = session.Terrain.GetHeight(character.Map, character.X, character.Y);
        if (ground is { } g)
            session.Logger.LogInformation("Рельеф: земля в ({X};{Y}) = {Ground:F2} (Z персонажа {Z:F2}, дельта {Delta:F2})",
                character.X, character.Y, g, character.Z, character.Z - g);

        // M5.1/M5.6: показать существ и гейм-объекты из БД мира вокруг (диф-видимость).
        await SpawnHandlers.RefreshVisibleNpcsAsync(session, character.Map, character.X, character.Y, ct);
        await SpawnHandlers.RefreshVisibleGameObjectsAsync(session, character.Map, character.X, character.Y, ct);

        // M5.3: зарегистрировать в мире и обоюдно спавнить с соседними игроками.
        session.Character = character;
        var player = new World.WorldPlayer { Guid = character.Guid, Character = character, Session = session };
        session.Player = player;
        await session.World.EnterWorldAsync(player, ct);

        // M7 #21: восстановить сохранённые переключатели (стойка воина/аура паладина/аспект охотника).
        await Auras.ReapplyPersistedAsync(session, ct);

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
                session.Logger.LogDebug("Повторная досылка соседей '{User}': {Msg}", session.Account, ex.Message);
            }
        }, ct);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgLogoutRequest)]
    public static async Task OnLogoutRequest(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        await session.SavePositionIfInWorldAsync(ct);
        await session.LeaveWorldAsync(ct); // снять с реестра + DESTROY соседям

        await session.SendAsync(WorldOpcode.SmsgLogoutResponse,
            new ByteWriter(5).UInt32(0).UInt8(1).ToArray(), ct); // reason=0, instant=1
        await session.SendAsync(WorldOpcode.SmsgLogoutComplete, [], ct);
        session.Logger.LogInformation("LOGOUT '{User}' → возврат к выбору персонажа", session.Account);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgLogoutCancel)]
    public static Task OnLogoutCancel(WorldSession session, IncomingPacket packet, CancellationToken ct)
        => session.SendAsync(WorldOpcode.SmsgLogoutCancelAck, [], ct);

    [WorldOpcodeHandler(WorldOpcode.CmsgTimeSyncResp)]
    public static Task OnTimeSyncResp(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var reader = packet.Reader();
        var counter = reader.UInt32();
        var clientTicks = reader.UInt32();
        // Матчим ответ с последним REQ → дельта часов (serverMs − clientTicks). RTT на LAN пренебрежим.
        if (counter == session.TimeSyncOutstanding)
        {
            session.ClockDeltaMs = session.TimeSyncSentMs - clientTicks;
            session.Logger.LogDebug("[timesync] '{User}': counter={C} clientTicks={T} → delta={D}мс",
                session.Account, counter, clientTicks, session.ClockDeltaMs);
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Шлёт SMSG_TIME_SYNC_REQ (новый счётчик) и запоминает время отправки — фундамент расчёта
    /// дельты часов клиента для нормализации времени движения. Зовётся при входе и периодически из тика.
    /// </summary>
    internal static async Task SendTimeSyncReqAsync(WorldSession session, CancellationToken ct)
    {
        var counter = session.TimeSyncCounter++;
        session.TimeSyncOutstanding = counter;
        session.TimeSyncSentMs = (uint)Environment.TickCount64;
        session.LastTimeSyncDispatchMs = Environment.TickCount64;
        await session.SendAsync(WorldOpcode.SmsgTimeSyncReq, new ByteWriter(4).UInt32(counter).ToArray(), ct);
    }

    /// <summary>
    /// SMSG_INITIAL_SPELLS (M9.3): книга заклинаний при входе. Набор = языковые спеллы расы ∪ стартовые
    /// абилки класса (playercreateinfo_spell) ∪ изученное у тренера (character_spell). Хардкод маг-спеллов
    /// из M6.4 (Fireball/Frostbolt/…) выдаём ТОЛЬКО магу (класс 8) — у остальных классов их в книге быть
    /// не должно. Заполняет session.KnownSpells (для HasSpell-проверок тренера). БД мира недоступна → фолбэк
    /// на языковые + (магу) боевые.
    /// </summary>
    private const byte ClassMage = 8;

    private static async Task SendInitialSpellsAsync(WorldSession session, Character character, CancellationToken ct)
    {
        var known = session.KnownSpells;
        known.Clear();
        foreach (var s in LanguageSpells.ForRace(character.Race))
            known.Add((uint)s);

        try
        {
            foreach (var s in await session.WorldDb.GetStartSpellsAsync(character.Race, character.Class, ct))
                known.Add(s);
        }
        catch (Exception ex)
        {
            session.Logger.LogDebug("INITIAL_SPELLS '{User}': стартовые спеллы из БД недоступны ({Msg})",
                session.Account, ex.Message);
        }

        // M6.4: боевые спеллы-заглушки умеет кастовать только маг (их эффекты хардкожены под мага).
        if (character.Class == ClassMage)
            foreach (var s in World.SpellCatalog.GrantedCombatSpells)
                known.Add((uint)s);

        // Изученное у тренера (персист).
        foreach (var s in await session.CharState.GetLearnedSpellsAsync(character.Guid, ct))
            known.Add(s);

        // M11 (#3): среди известных оставить в КНИГЕ только высший тир каждой профессии (низшие
        // superceded — иначе показывались бы «Горное дело (ученик)» и «(подмастерье)» вместе). Низшие
        // остаются в KnownSpells (для каста/валидации не важно), но в книгу не шлём. Один батч-запрос.
        session.ProfessionRankSpell.Clear();
        var hiddenLowerTiers = new HashSet<uint>();
        try
        {
            foreach (var tpl in await session.WorldDb.GetSpellsAsync(known.ToList(), ct))
            {
                if (World.Professions.SkillGrantedBy(tpl) is not { } g)
                    continue;
                if (session.ProfessionRankSpell.TryGetValue(g.SkillId, out var cur))
                {
                    var lower = g.Max > cur.Max ? cur.Spell : tpl.Id;
                    hiddenLowerTiers.Add(lower);
                    if (g.Max > cur.Max)
                        session.ProfessionRankSpell[g.SkillId] = (tpl.Id, g.Max);
                }
                else
                    session.ProfessionRankSpell[g.SkillId] = (tpl.Id, g.Max);
            }
        }
        catch (Exception ex)
        {
            session.Logger.LogDebug("INITIAL_SPELLS '{User}': дедуп профессий пропущен ({Msg})", session.Account, ex.Message);
        }

        var book = known.Where(s => !hiddenLowerTiers.Contains(s)).ToList();
        var w = new ByteWriter(8 + book.Count * 6)
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
        const int factionCount = 128; // mangostwo (wrath) = 0x80
        var w = new ByteWriter(4 + factionCount * 5);
        w.UInt32(factionCount);
        for (var i = 0; i < factionCount; i++)
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
