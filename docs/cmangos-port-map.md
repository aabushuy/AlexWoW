# Карта порта CMaNGOS → AlexWoW

> Source of truth для соответствия «CMaNGOS-WoTLK файл → наш C#-порт».
> Обновляется **перед** каждой задачей: новая строка с явной ссылкой на исходник.
>
> Стратегия и фазы — [strategy/cmangos-port.md](strategy/cmangos-port.md).
> Upstream: https://github.com/cmangos/mangos-wotlk (master = 3.3.5a).

## Легенда

- ✅ полностью портирован, проверен клиентом
- 🟡 базовый порт есть, не все ветки/механики
- ⬜ не начат
- 🚫 не портируем намеренно (см. [strategy → Phase 5](strategy/cmangos-port.md#phase-5-что-не-портируем-наша-оригинальная-инфраструктура))

«Полностью» = поведенчески эквивалентно эталону на проверенных сценариях, **не** означает байт-в-байт.

---

## Ядро (Spells / Combat / Auras)

| CMaNGOS файл | Назначение | Наш порт | Статус |
|---|---|---|---|
| `src/game/Spells/SpellMgr.cpp` | Загрузка/индексация `spell_template`, ранги, deprecated | [`World/SpellCatalog.cs`](../src/AlexWoW.WorldServer/World/SpellCatalog.cs) | 🟡 |
| `src/game/Spells/Spell.cpp` (cast pipeline) | Каст: prepare → check → effect | [`Handlers/SpellCastService.cs`](../src/AlexWoW.WorldServer/Handlers/SpellCastService.cs) + [`SpellCastCompletion.cs`](../src/AlexWoW.WorldServer/Handlers/SpellCastCompletion.cs) | 🟡 |
| `src/game/Spells/SpellEffects.cpp` | 100+ SPELL_EFFECT_* | [`Handlers/SpellEffectsService.cs`](../src/AlexWoW.WorldServer/Handlers/SpellEffectsService.cs) | 🟡 |
| `src/game/Spells/SpellAuras.cpp` | APPLY/REMOVE аур, ауры-сторы | [`Handlers/AuraService.cs`](../src/AlexWoW.WorldServer/Handlers/AuraService.cs), [`AuraPersistenceService.cs`](../src/AlexWoW.WorldServer/Handlers/AuraPersistenceService.cs), [`World/ActiveAura.cs`](../src/AlexWoW.WorldServer/World/ActiveAura.cs) | 🟡 |
| `src/game/Spells/UnitAuraProcHandler.cpp` | Прок-флаги, события (~5000 строк) | [`Handlers/ProcService.cs`](../src/AlexWoW.WorldServer/Handlers/ProcService.cs) | 🟡 |
| `src/game/Spells/SpellAuraDefines.h` (AURA_STATE) | UNIT_FIELD_AURASTATE окна (Revenge, Overpower, …) | [`Handlers/AuraStateService.cs`](../src/AlexWoW.WorldServer/Handlers/AuraStateService.cs) | 🟡 |
| `src/game/Spells/Periodics.cpp` | Periodic-ауры (DoT/HoT/leech) | [`Handlers/PeriodicsService.cs`](../src/AlexWoW.WorldServer/Handlers/PeriodicsService.cs) | 🟡 |
| `src/game/Spells/Absorb.cpp` | Absorb-щиты (PW:S, Ice Barrier, …) | [`Handlers/AbsorbShieldService.cs`](../src/AlexWoW.WorldServer/Handlers/AbsorbShieldService.cs) | 🟡 |
| `src/game/Spells/Dispel.cpp` | Dispel / Spellsteal / Purge | [`Handlers/DispelService.cs`](../src/AlexWoW.WorldServer/Handlers/DispelService.cs) | 🟡 |
| `src/game/Spells/SpellModifier.cpp` (auras 107/108) | Модификаторы талантов | [`Handlers/SpellModifierService.cs`](../src/AlexWoW.WorldServer/Handlers/SpellModifierService.cs) | 🟡 |
| `src/game/Spells/CrowdControl.cpp` | Stun/Fear/Polymorph/Disorient (AoE/DR) | [`Handlers/CrowdControlService.cs`](../src/AlexWoW.WorldServer/Handlers/CrowdControlService.cs) | 🟡 |
| `src/game/Spells/scripts/Spell_Warrior.cpp` | Класс-спеллы Warrior | (in [`SpellEffectsService.cs`](../src/AlexWoW.WorldServer/Handlers/SpellEffectsService.cs)) | 🟡 |
| `src/game/Spells/scripts/Spell_Paladin.cpp` | Seals / Auras / Judgement | [`Handlers/SealService.cs`](../src/AlexWoW.WorldServer/Handlers/SealService.cs) + effects | 🟡 |
| `src/game/Spells/scripts/Spell_Rogue.cpp` | Combo points, poisons | [`Handlers/ComboPointService.cs`](../src/AlexWoW.WorldServer/Handlers/ComboPointService.cs), [`PoisonService.cs`](../src/AlexWoW.WorldServer/Handlers/PoisonService.cs) | 🟡 |
| `src/game/Spells/scripts/Spell_Hunter.cpp` | — | ⬜ нет (петы будут отдельно) | ⬜ |
| `src/game/Spells/scripts/Spell_DK.cpp` | Runes | [`Handlers/RuneService.cs`](../src/AlexWoW.WorldServer/Handlers/RuneService.cs) | 🟡 |
| `src/game/Spells/scripts/Spell_Mage.cpp` | — | (in effects/auras) | 🟡 |
| `src/game/Spells/scripts/Spell_Warlock.cpp` | Soul shards, петы | ⬜ | ⬜ |
| `src/game/Spells/scripts/Spell_Priest.cpp` | — | (in effects/auras) | 🟡 |
| `src/game/Spells/scripts/Spell_Shaman.cpp` | Totems, weapon imbues | [`Handlers/ImbueService.cs`](../src/AlexWoW.WorldServer/Handlers/ImbueService.cs), totems ⬜ | 🟡 |
| `src/game/Spells/scripts/Spell_Druid.cpp` | Формы, hybrid | (in effects/auras) | 🟡 |
| `src/game/Combat/Unit.cpp` (damage/melee) | Боевая модель, авто-атака | [`Handlers/PlayerMeleeService.cs`](../src/AlexWoW.WorldServer/Handlers/PlayerMeleeService.cs), [`CombatOpcodeHandlers.cs`](../src/AlexWoW.WorldServer/Handlers/CombatOpcodeHandlers.cs), [`World/CombatStats.cs`](../src/AlexWoW.WorldServer/World/CombatStats.cs) | 🟡 |
| `src/game/Combat/Unit.cpp` (ресурсы) | Ярость/энергия/мана/руны | [`Handlers/CombatResourcesService.cs`](../src/AlexWoW.WorldServer/Handlers/CombatResourcesService.cs), [`ManaRegenService.cs`](../src/AlexWoW.WorldServer/Handlers/ManaRegenService.cs), [`RegenService.cs`](../src/AlexWoW.WorldServer/Handlers/RegenService.cs) | 🟡 |

## Прогрессия / Персонаж

| CMaNGOS файл | Назначение | Наш порт | Статус |
|---|---|---|---|
| `src/game/Player/Player.cpp` (XP/level) | XP/уровень/статы | [`Handlers/ProgressionService.cs`](../src/AlexWoW.WorldServer/Handlers/ProgressionService.cs), [`World/LevelStore.cs`](../src/AlexWoW.WorldServer/World/LevelStore.cs), [`StatStore.cs`](../src/AlexWoW.WorldServer/World/StatStore.cs) | ✅ |
| `src/game/Player/PlayerTalents.cpp` | Дерево талантов, learn/reset | [`Handlers/TalentHandlers.cs`](../src/AlexWoW.WorldServer/Handlers/TalentHandlers.cs), [`World/TalentMath.cs`](../src/AlexWoW.WorldServer/World/TalentMath.cs) | 🟡 |
| `src/game/Player/Player.cpp` (skills/professions) | Скиллы и профессии | [`Handlers/SkillsService.cs`](../src/AlexWoW.WorldServer/Handlers/SkillsService.cs), [`CraftingService.cs`](../src/AlexWoW.WorldServer/Handlers/CraftingService.cs), [`World/Professions.cs`](../src/AlexWoW.WorldServer/World/Professions.cs), [`PlayerSkills.cs`](../src/AlexWoW.WorldServer/World/PlayerSkills.cs) | 🟡 |
| `src/game/Trainer/Trainer.cpp` | Классовые/проф тренеры | [`Handlers/TrainerHandlers.cs`](../src/AlexWoW.WorldServer/Handlers/TrainerHandlers.cs), [`TrainerCatalogService.cs`](../src/AlexWoW.WorldServer/Handlers/TrainerCatalogService.cs) | 🟡 |
| `src/game/Spells/SpellLearn.cpp` | Изучение спеллов | [`Handlers/SpellLearnService.cs`](../src/AlexWoW.WorldServer/Handlers/SpellLearnService.cs) | 🟡 |

## Мир / ИИ / Видимость

| CMaNGOS файл | Назначение | Наш порт | Статус |
|---|---|---|---|
| `src/game/Maps/Map.cpp` | Карты, гриды, тики | [`World/WorldState.cs`](../src/AlexWoW.WorldServer/World/WorldState.cs), [`WorldTick.cs`](../src/AlexWoW.WorldServer/World/WorldTick.cs), [`WorldUpdateLoop.cs`](../src/AlexWoW.WorldServer/World/WorldUpdateLoop.cs) | 🟡 |
| `src/game/Maps/Cell.cpp` / visibility | Грид-видимость | [`Handlers/VisibilityService.cs`](../src/AlexWoW.WorldServer/Handlers/VisibilityService.cs), [`World/PlayerVisibility.cs`](../src/AlexWoW.WorldServer/World/PlayerVisibility.cs) | ✅ |
| `src/game/Movement/*` (MoveSplineInit и т.п.) | Движение игроков | [`Handlers/MovementHandlers.cs`](../src/AlexWoW.WorldServer/Handlers/MovementHandlers.cs) | 🟡 |
| `src/game/AI/CreatureEventAI.cpp` | ИИ существ | [`Handlers/CreatureCombatAI.cs`](../src/AlexWoW.WorldServer/Handlers/CreatureCombatAI.cs), [`World/CreatureDirector.cs`](../src/AlexWoW.WorldServer/World/CreatureDirector.cs) | 🟡 |
| `src/game/Object/Creature.cpp` | Существа: спавн, реген, лут | [`Handlers/SpawnHandlers.cs`](../src/AlexWoW.WorldServer/Handlers/SpawnHandlers.cs), [`KillRewardService.cs`](../src/AlexWoW.WorldServer/Handlers/KillRewardService.cs), [`World/CreatureLoot.cs`](../src/AlexWoW.WorldServer/World/CreatureLoot.cs) | 🟡 |
| `src/game/Loot/LootMgr.cpp` | Лут-таблицы, ролл | [`Handlers/LootHandlers.cs`](../src/AlexWoW.WorldServer/Handlers/LootHandlers.cs) | 🟡 |
| `src/game/GameObject/GameObject.cpp` | Гейм-объекты, USE | [`Handlers/GameObjectUseHandlers.cs`](../src/AlexWoW.WorldServer/Handlers/GameObjectUseHandlers.cs) | 🟡 |
| `src/game/Object/Gossip.cpp` | Госсип-меню NPC | [`Handlers/GossipService.cs`](../src/AlexWoW.WorldServer/Handlers/GossipService.cs) | 🟡 |
| `src/game/Reputation/ReputationMgr.cpp` | Фракции, репутация | [`World/FactionStore.cs`](../src/AlexWoW.WorldServer/World/FactionStore.cs) | 🟡 |

## Предметы / Инвентарь / Квесты

| CMaNGOS файл | Назначение | Наш порт | Статус |
|---|---|---|---|
| `src/game/Object/Item.cpp` | Предметы, экип, чары | [`Handlers/ItemHandlers.cs`](../src/AlexWoW.WorldServer/Handlers/ItemHandlers.cs), [`InventoryOpcodeHandlers.cs`](../src/AlexWoW.WorldServer/Handlers/InventoryOpcodeHandlers.cs), [`BagInventory.cs`](../src/AlexWoW.WorldServer/Handlers/BagInventory.cs) | 🟡 |
| `src/game/Object/Item.cpp` (move/split) | Перемещение по сумкам | [`Handlers/InventoryMoveService.cs`](../src/AlexWoW.WorldServer/Handlers/InventoryMoveService.cs), [`InventoryClientSync.cs`](../src/AlexWoW.WorldServer/Handlers/InventoryClientSync.cs) | 🟡 |
| `src/game/Vendor/*` | Вендор | [`Handlers/VendorHandlers.cs`](../src/AlexWoW.WorldServer/Handlers/VendorHandlers.cs) | 🟡 |
| `src/game/Quest/Quest.cpp` | Квесты: приём/прогресс/сдача | [`Handlers/QuestOpcodeHandlers.cs`](../src/AlexWoW.WorldServer/Handlers/QuestOpcodeHandlers.cs), [`QuestDialogService.cs`](../src/AlexWoW.WorldServer/Handlers/QuestDialogService.cs), [`QuestProgressService.cs`](../src/AlexWoW.WorldServer/Handlers/QuestProgressService.cs), [`QuestGiverStatusService.cs`](../src/AlexWoW.WorldServer/Handlers/QuestGiverStatusService.cs), [`World/QuestStore.cs`](../src/AlexWoW.WorldServer/World/QuestStore.cs) | 🟡 |

## Сеть / Сессия

| CMaNGOS файл | Назначение | Наш порт | Статус |
|---|---|---|---|
| `src/game/WorldSocket.cpp` + `WorldSession.cpp` | Транспорт, диспетчер опкодов | [`Net/WorldListener.cs`](../src/AlexWoW.WorldServer/Net/WorldListener.cs), [`Net/WorldSession.cs`](../src/AlexWoW.WorldServer/Net/WorldSession.cs), [`Handlers/WorldPacketRouter.cs`](../src/AlexWoW.WorldServer/Handlers/WorldPacketRouter.cs) | 🟡 |
| `src/realmd/AuthSocket.cpp` (SRP6) | Логин-сервер | [`AlexWoW.AuthServer/Net/AuthSession.cs`](../src/AlexWoW.AuthServer/Net/AuthSession.cs) + [`AlexWoW.Cryptography/Srp6/*`](../src/AlexWoW.Cryptography/) | ✅ |
| `src/game/WorldSession.cpp` (login flow) | Вход в мир, аддоны | [`Handlers/AuthHandlers.cs`](../src/AlexWoW.WorldServer/Handlers/AuthHandlers.cs), [`AuthChallengeSender.cs`](../src/AlexWoW.WorldServer/Handlers/AuthChallengeSender.cs), [`LoginSequenceService.cs`](../src/AlexWoW.WorldServer/Handlers/LoginSequenceService.cs), [`AddonProtocol.cs`](../src/AlexWoW.WorldServer/Handlers/AddonProtocol.cs) | 🟡 |
| `src/game/WorldSession.cpp` (chat) | Чат `/say`/каналы | [`Handlers/ChatHandlers.cs`](../src/AlexWoW.WorldServer/Handlers/ChatHandlers.cs), [`ChatNotifier.cs`](../src/AlexWoW.WorldServer/Handlers/ChatNotifier.cs) | 🟡 |
| `src/game/TimeSync.cpp` | Таймсинк клиента | [`Handlers/TimeSyncService.cs`](../src/AlexWoW.WorldServer/Handlers/TimeSyncService.cs) | 🟡 |

## ⬜ Не начато (приоритет — см. strategy → Phase 3)

| CMaNGOS файл(ы) | Назначение | Куда ляжет | Статус |
|---|---|---|---|
| `src/game/Group/Group.cpp` + `GroupMgr.cpp` | Партии/рейды (препрек для подземелий) | `Handlers/Group/*` (новый каталог) | ⬜ |
| `src/game/Guild/Guild.cpp` + `GuildMgr.cpp` | Гильдии (~47 опкодов) | `Handlers/Guild/*` | ⬜ |
| `src/game/Pet/Pet.cpp` + `PetAI.cpp` | Хантер/локер питомцы | `Handlers/Pet/*` | ⬜ |
| `src/game/Mail/Mail.cpp` + `MailMgr.cpp` | Почта (12 опкодов) | `Handlers/Mail/*` | ⬜ |
| `src/game/Social/SocialMgr.cpp` | Друзья/whisper/каналы (58 опкодов) | `Handlers/Social/*` | ⬜ |
| `src/game/LFG/LFGMgr.cpp` | LFG (29 опкодов) | `Handlers/LFG/*` | ⬜ |
| `src/game/AuctionHouse/*` | Аукцион (14 опкодов) | `Handlers/Auction/*` | ⬜ |
| `src/game/BattleGround/*` | BG / Arena (40 опкодов) | `Handlers/PvP/*` | ⬜ |
| `src/game/Maps/InstanceData.cpp` + ScriptDev2 | Подземелья + boss-скрипты | `Handlers/Instances/*` + `Scripts/*` | ⬜ |

## 🚫 Не портируем — наша оригинальная инфраструктура

(см. [strategy → Phase 5](strategy/cmangos-port.md#phase-5-что-не-портируем-наша-оригинальная-инфраструктура))

- Spell QA Harness (`Handlers/SpellTestHarnessService.cs`, `SpellTestCaptureService.cs`, `SpellTestRequestService.cs`)
- Канбан-доска (БД `project`, Web/Pages/Board, Kanban-сервисы)
- Веб-админка / регистрация (Razor Pages в `AlexWoW.Web`)
- Dev-команды (`Handlers/Dev/*`: `.cast`, `.learn`, `.tele`, …)
- tools/MapExtractor, tools/MmapGen, tools/regression-import

---

## Как обновлять карту

1. **Перед** началом задачи: добавить/обновить строку в нужной секции (CMaNGOS-исходник + где ляжет).
2. После реализации: подвинуть статус (⬜ → 🟡 → ✅).
3. В commit-сообщении — `port(cmangos): <система> from src/game/X/Y.cpp` (см. [CONTRIBUTING.md](../CONTRIBUTING.md)).
4. Если файл переехал — обновить путь, не оставлять мёртвых ссылок.

CMaNGOS-исходники доступны по адресу `https://github.com/cmangos/mangos-wotlk/blob/master/<путь>`.
Локально: read-only зеркало на homeserver — `\\homeserver\WowProject\repos\mangos-wotlk\`.
