# Devlog M7 — техдолг, рефакторинг, тест-инструменты

Веха **M7** (Vikunja project #10) — баги, технический долг и рефакторинг поверх M6/M9. Задачи и статусы —
в Vikunja; здесь — журнал реализации (решения и грабли). Эталон — CMaNGOS-WotLK. Все срезы:
build 0/0 + тесты 12/12 + деплой + smoke-тест клиентом → коммит на `main`.

---

## Принцип: код по SOLID

Зафиксировано с пользователем: код пишем по **SOLID** (см. `architecture.md` §0). При появлении «жирного»
класса с несколькими ответственностями — разбиваем на SRP-классы (хаб-фасад + focused-коллабораторы,
нулевой churn у потребителей). Применено к DAL (#23–25) и `WorldState` (#30).

---

## #23 — Рефактор DAL: EF Core/Pomelo для `alexwow_auth` + Dapper для `mangos`

**Проблема:** SQL захардкожен строками в god-классах `AuthDatabase`/`CharactersDatabase`/`WorldDatabase`;
схема `alexwow_auth` создавалась вручную (`CREATE TABLE IF NOT EXISTS` + `ALTER … catch 1060`); часть
чтений маппилась руками через `IDictionary<string,object>`.

Сделано 5 срезами (каждый — отдельный деплой+тест):
1. **Интерфейсы DAL** (`src/AlexWoW.Database/Abstractions/`) поверх существующего Dapper — потребители
   переведены на абстракции, поведение не менялось.
2. **EF-каркас**: `Entities/` (10 сущностей) + `AuthDbContext` (Fluent ТОЧНО под текущую прод-схему,
   сверено с живым `SHOW CREATE TABLE`) + миграция `InitialCreate` + design-time фабрика.
3. **Cutover auth** на EF (`EfAccountRepository` поверх пул-фабрики `AddPooledDbContextFactory`).
4. **Cutover characters** на EF (`EfCharacterStore`, ~30 методов на LINQ/`ExecuteUpdate/Delete`).
5. **Удаление dead-кода** + CLI на EF (`CliRepository`) → Dapper для нашей БД убран полностью.

**Ключевые решения / грабли:**
- ⚠️ **Под EF Core 10 релиза Pomelo нет** (стабильный максимум — 9.0.0/EF Core 9). EF9-сборки работают на
  рантайме net10 через roll-forward; глобальный `dotnet ef` 10.0.7 новее рантайма → совместим.
- **Baseline существующего прода** (`deploy/sql/ef-baseline-alexwow_auth.sql`): таблицы уже есть с живыми
  данными → НЕ запускаем `CREATE TABLE` из `InitialCreate`, а вручную вписываем строку в
  `__EFMigrationsHistory` → `MigrateAsync` на старте no-op. На чистой/dev БД `MigrateAsync` создаёт всё с нуля.
- **Пул-фабрика контекста** (`AddPooledDbContextFactory` + контекст на операцию) — singleton-safe при
  многопоточных долгоживущих сессиях; повторяет прежнюю модель Dapper «подключение на запрос».
- **Мигрирует только auth-сервер** (world зовёт EnsureSchema лишь у char-стора) → гонки миграций нет.
- Точное соответствие схемы: `uint`→`int unsigned`, `byte`→`tinyint unsigned`, `ushort`→`smallint unsigned`,
  `binary(32)/(40)` через `HasColumnType`, `timestamp`+`HasDefaultValueSql("CURRENT_TIMESTAMP")`,
  single-PK не-AI колонку (`declined.owner_guid`) — `ValueGeneratedNever()`.

## #24 — SOLID: разбить `EfCharacterStore`/`EfAccountRepository`

`EfCharacterStore` (жирный, 4 ответственности) → 4 SRP-класса: `EfCharacterRepository` (ядро+склонения),
`EfInventoryRepository`, `EfQuestRepository`, `EfCharacterStateRepository`. Фасад `ICharacterStore` удалён;
`WorldSession` отдаёт 4 узких свойства (`Characters`/`Items`/`Quests`/`CharState`) — потребитель зависит
только от нужного (ISP). `EfAccountRepository` разбит: account-операции + `EfRealmRepository`
(`IRealmRepository`) + `AuthSchemaInitializer` (`ISchemaInitializer` — миграции+сид реалма).

Бонус: **Models почищены** — один тип на файл; иммутабельные DTO → `record`; мутируемые
(`InventoryItem`/`Character`/`Account`) оставлены class.

## #25 — SOLID: `WorldDatabase` → 9 focused-репозиториев

God-класс `WorldDatabase` (23 метода, read-only `mangos`) разбит на 9 focused Dapper-репозиториев в
`Repositories/World/` (`Creature`/`GameObject`/`ItemTemplate`/`Vendor`/`Trainer`/`Loot`/`QuestTemplate`/
`Faction`/`PlayerData`) + база `MangosRepositoryBase`. 9 узких интерфейсов (`Abstractions/World/`);
`IWorldRepository` стал **композитным фасадом** (делегирует). `*Store` зависят от узких интерфейсов
(`FactionStore`→`IFactionRepository`, `Stat/LevelStore`→`IPlayerDataRepository`,
`QuestStore`→`IQuestTemplateRepository`); `WorldSession.WorldDb` остался фасадом.

**Итог DAL:** EF (alexwow_auth) = focused EF-репозитории; Dapper (mangos) = focused Dapper-репозитории;
god-классов `AuthDatabase`/`CharactersDatabase`/`WorldDatabase`/`EfCharacterStore` больше нет.

---

## #26 — Нортшир: тренеры с полным набором умений всех классов (вкл. ДК)

Отладочные тренеры M9.3 (guid 9000001..) оказались нортширскими **стартовыми** тренерами с урезанными
шаблонами (template 5/20/40/50/80/90 → 5-6 спеллов). Заменены 10 кастомными тренерами с **полной
прогрессией класса** — `tools/scripts/northshire-class-trainers.sql`.

- Кастомные `entry 990001..990011` (клон реального классового тренера + переопределение), спавн
  `guid 9000001..9000011` у точки старта. **Faction=35** (`hostileMask=0` → дружелюбны/нейтральны ко всем
  расам), **TrainerRace=0** → доступны любому классу/расе; флаги trainer|gossip (0x11); полный список
  спеллов из **канонического trainer-template класса** напрямую в `npc_trainer`.
- Шаблоны: Воин=11, Паладин=21, Охотник=31, Разбойник=41, Жрец=51, Шаман=71, **Маг=81 (НЕ 71!)**,
  Чернокнижник=91, Друид=111; ДК (класс 6) — прямой `npc_trainer` (entry 28471).
- **Грабля:** «максимальный по числу спеллов» тренер ненадёжен — маг-тренер 27704 ошибочно ссылается на
  ШАМАНСКИЙ template 71. Канонический шаблон класса = **самый используемый** среди тренеров этого класса.

## #27 — Гейт рангов заклинаний по уровню

У тренера ВСЕ ранги были доступны (зелёные) на 1-м уровне: `npc_trainer.reqlevel` в дампе почти везде 0.

**КЛЮЧЕВОЕ: в `mangos` есть `spell_template` — ПОЛНЫЙ дамп Spell.dbc** (50889 строк: `Id`/`SpellLevel`/
`BaseLevel`/`MaxLevel`/`SpellName`/`Rank1..16`/`EffectApplyAuraName*`/каст-таймы/…). **Spell.dbc извлекать
не нужно — всё в БД.** `SpellLevel`(=BaseLevel) = точный уровень изучения ранга (Frostbolt r1=4…r16=79).

Фикс серверный: `TrainerSpell.SpellLevel` (LEFT JOIN `spell_template` в `TrainerRepository`), гейт в
`TrainerHandlers.StateFor` по `max(reqlevel, spellLevel)` → высшие ранги красные до нужного уровня.
*Для спелл-системы M6.4 (каст-таймы/школы/эффекты хардкожены) использовать `spell_template`, не парсер DBC.*
Отложено (опц.): ранг-цепочки SUPERCEDED через `spell_chain`.

---

## #28 / #29 — Тренировочный манекен + дев-команда `.dummy`

Стационарная пассивная цель 80 ур. для проверки навыков.

- **#28** SQL `tools/scripts/northshire-training-dummy.sql`: клон Advanced Training Dummy (24792) →
  **entry 990020** (ур.80, модель 3019, фракция 7=жёлтый/атакуемый). **Код распознаёт entry 990020**
  (`Npcs.IsTrainingDummy`): HP **50 млн** вместо формулы `MaxHealthFor=25+lvl*12` (`SpawnHandlers`) и
  **пассивность** — гейт в `CombatHandlers.EnterCreatureCombatAsync` (единая точка входа существа в бой —
  и ответка, и авто-агро; манекен не входит → не бьёт/не агрится).
  - **Грабля:** HP существа берётся из формулы `MaxHealthFor`, а НЕ из `creature_template` → для большого
    HP нужен спец-кейс.
- **#29** дев-команда **`.dummy`** (гейт `is_admin`): `WorldState` → `CreatureDirector.SummonTrainingDummyAsync`
  телепортирует манекен (тот же GUID, что у статичного спавна) на ~3 ярда перед игроком (DESTROY+CREATE
  наблюдателям, сброс HP/боя).
  - **Грабля:** `WorldCreature.Home*` — `init` (нельзя переприсвоить при перемещении; X/Y/Z мутабельны).
- Дев-команды (`Handlers/DevCommands.cs`, через чат с `.`, гейт `session.IsAdmin`): `.level`, `.xp`,
  `.additem`, `.learn`, `.buff`, `.unbuff`, `.dummy`.

---

## #30 — Рефактор `WorldState` по SOLID

God-класс `WorldState` (449 строк, ~7 ответственностей) разбит на **ядро-хаб + 3 SRP-коллаборатора**
(в `World/`), которые хаб композирует и делегирует — **API для хендлеров/сессий не изменился**
(`session.World.X` работает как прежде, нулевой churn):

- **`WorldState`** (хаб, ~280 строк): реестр `_players`/`_creatures`, пространственные запросы
  (`PlayersInRangeOf`/`ObserversOf`), рассылка (`Broadcast*`), урон (`ApplyCreature/PlayerDamage`),
  фасады сторов (`Stats`/`Quests`/`Levels`/`IsHostile`).
- **`PlayerVisibility`** — вход/выход/`RefreshVisiblePlayers`/`RelayMovement`/досылка экипировки соседей.
- **`CreatureDirector`** — `MoveCreature`/`FaceCreature`/`FindGroundPath`/`RespawnCreature`/
  `SummonTrainingDummy` (держит `_splineId` и `navmesh`).
- **`WorldTick`** — `UpdateAsync` (серверный тик по-игроку + по-существу).

Коллабораторы создаются в ctor `WorldState` (берут `this`), не в DI. `Protocol` — фокусные билдеры
пакетов / константы, god-классов нет, разбивать нечего. **Паттерн для будущих god-классов: хаб-фасад +
SRP-коллабораторы.**

---

## #34–#45 — Большой рефакторинг: статики → DI, code-style, разбор god-классов (S0–S11)

Сквозной рефакторинг всех 7 проектов по принципам SOLID/KISS/DRY: статические классы оставлены
только хелперам (билдеры пакетов `Protocol/*`, константы, чистая математика, extensions), вся
игровая логика и сервисы — stateless DI-синглтоны. 12 срезов (S0–S11), каждый собирается,
бутается и коммитится отдельно; инвариант — **84 опкода в логе старта** на каждом срезе.

**S0 — code-style базис (#34):** `docs/code-style.md` (конвенции Microsoft C# + практика репо),
`.editorconfig` (нейминг, file-scoped namespaces, var), `Directory.Build.props` (net10.0, Nullable,
`TreatWarningsAsErrors`); csproj очищены от дублей.

**S1 — DI-роутер (#35):** `WorldPacketRouter` — DI-синглтон; модули `IOpcodeHandlerModule`
регистрируются assembly-сканом (`AddWorldOpcodeHandlers()`: конкретный тип + форвард на маркер),
роутер собирает instance-методы с `[WorldOpcodeHandler]` в `FrozenDictionary`. Сессии создаёт
`WorldSessionFactory` с parameter object `WorldSessionServices` — service locator запрещён.

**S2–S8 — конверсия кластеров (#36–#42):** простые модули → спеллы (разбор `SpellCaster` на
`SpellCastService`/`SpellCastCompletion`/`SpellGoSender`) → бой (разбор `CombatHandlers` на
`CombatOpcodeHandlers`/`PlayerMeleeService`/`CreatureCombatAI`/`RegenService`) → квесты/лут
(разбор `QuestHandlers` на модуль + `QuestProgressService`/`QuestDialogService`/
`QuestGiverStatusService`/`GossipService`; `KillRewardService`) → инвентарь/тренеры/прогрессия
(разбор `InventoryHandlers`; `TrainerCatalogService`, `SpellLearnService`, `SkillsService`,
`ProgressionService`) → вход/спавн/телепорт (`LoginSequenceService`, `VisibilityService`,
`TimeSyncService`) → dev-команды (реестр над `IEnumerable<IDevCommand>`, `ChatNotifier`).

**S9 (#43):** с `WorldSession` сняты все repo-мосты (потребители на ctor-инъекции), состояние
сгруппировано в компоненты `Net/SessionState/*` с `Reset()` (семантика `LeaveWorldAsync` 1:1).

**S10 (#44):** AuthServer CLI → `ICliCommand` из единого Host-контейнера (минус `CliRepository`
и дубли конфига); `RealmListBuilder` вынесен из `AuthSession`.

**S11 (#45):** Web `AuthSession` → extension-методы; стиль-проход остальных проектов;
`dotnet format` по решению.

**Ключевые решения / грабли:**
- **Порядок конверсии — сверху вниз по графу вызовов**: instance-код может звать статики, статики
  instance-сервисы — НЕТ. Мост на переходный период: роутер сканировал и модули, и легаси-статики
  (модуль приоритетнее), а статикам сервисы публиковались **свойствами сессии** («мост до Sx») —
  статики и так получают session, глобального состояния не появилось. Фолбэк и мосты сняты в S7–S9.
- **Страховка от потери опкодов**: после снятия фолбэка роутер валит старт, если в сборке остался
  статический метод с `[WorldOpcodeHandler]` (имена виновников в исключении).
- **Циклы статических вызовов** разорваны выделением сервисов: `SpellGoSender`
  (SpellCaster↔SpellToggles), `AuraPersistenceService` (Auras↔Periodics), `QuestProgressService`
  (InventoryGrant↔QuestHandlers), `GossipService` (Quest↔Trainer↔Vendor), `VisibilityService`
  (Teleport→SpawnHandlers), `TimeSyncService` (WorldTick→WorldEntryHandlers).
- **Инъекция модуля в модуль**: модуль регистрируется как конкретный singleton + форвард на маркер —
  один экземпляр и для роутера, и для зависимостей.
- **DI-цикл реестра dev-команд**: `.help` нельзя дать реестр через ctor (реестр ← команды ← реестр) —
  список команд передаётся через `DevCommandContext.AllCommands` от диспетчера.
- **`dotnet format` и EF-миграции**: сгенерированные `Migrations/*` пометить `generated_code = true`
  в `.editorconfig`, иначе format переписывает их под file-scoped namespaces (лишний чёрн).
- Сервисы обязаны быть **stateless** (всё состояние — в `WorldSession`/`WorldState`); допустимые
  исключения — иммутабельные кэши (`SpellCatalog`) и атомарные счётчики (`_splineId`).
