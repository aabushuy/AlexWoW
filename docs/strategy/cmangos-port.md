# Стратегия: порт CMaNGOS-WoTLK на C#

> **Source of truth** для стратегического разворота проекта. Все портирующие задачи и принятие решений сверяются с этим документом.

## Контекст

Объективно: написать сервер WoW 3.3.5a с нуля «по спецификации» — слишком долго. Текущая готовность ~58% (по доменам), но это **широта**; внутри много 🟡/⬜. PvP/гильдии/петы/группы/инстансы — нулевые. Чтобы получить полноценно играбельный сервер, нам нужны 1000+ механик, которые в [CMaNGOS-WoTLK](https://github.com/cmangos/mangos-wotlk) уже **полноценно реализованы и проверены сообществом**.

Принятые решения:

1. **Источник истины — CMaNGOS-WoTLK** (797 .cpp / 415 .h). Master ветка = 3.3.5a из коробки, наша БД `mangos` совместима с их дампом, proc-аура `Counterattack/Overpower/Revenge` уже реализованы в `UnitAuraProcHandler.cpp`.
2. **Порт на C#, лоб-в-лоб**: переписываем систему за системой, имя-в-имя где возможно, чтобы будущий рефактор шёл с минимальным риском. Сохраняем весь .NET-стек: WorldServer/AuthServer (C#), Web/канбан/QA-Harness, EF Core, tools/, CI/CD.
3. **Лицензия — GPL-2.0**: репо публичный форк под GPL-2.0. Порт на другой язык считается derivative work.
4. **Никакой rip-and-replace**: не бросаем .NET, не форкаем CMaNGOS как C++-проект. Мы — C#-проект, который использует CMaNGOS как эталонную спецификацию. Никакого C++-кода в репо (кроме отдельных tools/ типа MapExtractor).

Что получаем: сохраняем уникальные ценности (Spell QA Harness, регрессионные тикеты 1900+, dev-команды, Web-канбан/дашборд, `.cast`/`.learn`) и одновременно — **ясный, конечный roadmap**: закончим, когда все системы CMaNGOS портированы. Каждая система — отдельный PR/коммит с явной ссылкой на исходник CMaNGOS.

---

## Phase 0. Pre-publish аудит + очистка репо

**ДО** публикации. Отдельная задача, может занять несколько сессий.

Чек-лист:

1. **Секреты в истории git**: `git log --all --full-history -S 'kb_' -S 'tk_' -S 'ZAQxsw' -S 'ACCESS_TOKEN'` — поиск токенов. Если что-то есть — `git filter-repo` или новый чистый репо без истории.
2. **Реальные секреты сейчас**: проверить `/data/docker/alexwow-config/.env`, `deploy/SECRETS.md` (только шаблоны, не значения), `.mcp.json`, `appsettings.*.json`.
3. **Мёртвый код/файлы**: `tools/WowLauncher/` (удалено — будет на .NET), unreferenced сервисы.
4. **Dead-deps**: `<PackageReference>` в csproj без использования. Лицензии third-party (`tools/MapExtractor/Foole.Mpq/`).
5. **Code-style**: `dotnet format`, актуальный `.editorconfig`.
6. **Документация**: переписать `README.md` (что/на чём/как запустить + link на CMaNGOS), актуализировать `docs/architecture.md`, `docs/onboarding/`.
7. **CONTRIBUTING.md**: PR-flow, code-style, commit-convention.
8. **Issue templates**: `.github/ISSUE_TEMPLATE/` для bug/feature/regression.
9. **`.gitignore`**: убрать упоминания удалённых директорий, проверить покрытие секретов/клиента.
10. **CI**: решить про self-hosted vs GitHub-hosted runners для public-репо (см. `deploy/RUNNERS.md` про fork-PR approval).
11. **Security review**: `gitleaks` или ручной диф последних 50 коммитов.

Результат: чистый репо, секреты убраны, документация актуальная, готов к публикации.

---

## Phase 1. Публикация репо и лицензия

1. **`LICENSE`** (GPL-2.0, полный текст с https://www.gnu.org/licenses/gpl-2.0.txt).
2. **Public** на GitHub (`aabushuy/AlexWoW`).
3. **README**: «WoW 3.3.5a server, C# port of CMaNGOS-WoTLK with custom Web-admin/QA infrastructure», ссылка на upstream.
4. **`NOTICE.md`**: список upstream — CMaNGOS-WoTLK (GPL-2.0).
5. **Header в портированных файлах**:
   ```csharp
   // Порт CMaNGOS-WoTLK: src/game/Spells/SpellMgr.cpp
   // (https://github.com/cmangos/mangos-wotlk). GPL-2.0.
   ```
6. **Финальная проверка**: `.env`, токены не в коммитах.

---

## Phase 2. Карта `docs/cmangos-port-map.md`

Документ-карта где какая система CMaNGOS портируется в нашу структуру. Поддерживается в актуальном состоянии.

| CMaNGOS файл | Назначение | Наш порт | Статус |
|---|---|---|---|
| `src/game/Spells/SpellMgr.cpp` | Парсер spell_template | `src/AlexWoW.WorldServer/World/SpellCatalog.cs` | 🟡 |
| `src/game/Spells/UnitAuraProcHandler.cpp` | Прок-флаги и события | `src/AlexWoW.WorldServer/Handlers/ProcService.cs` | 🟡 |
| `src/game/Combat/Unit.cpp` (Damage) | Боевая модель | `src/AlexWoW.WorldServer/World/CombatStats.cs` + `Handlers/CreatureCombatAI.cs` | 🟡 |
| `src/game/Spells/scripts/Spell_Warrior.cpp` | Класс-спеллы | `src/AlexWoW.WorldServer/Handlers/Spells/...` | ⬜ |
| `src/game/AI/CreatureEventAI.cpp` | ИИ существ | `src/AlexWoW.WorldServer/Handlers/CreatureCombatAI.cs` | 🟡 |
| `src/game/Guilds/Guild.cpp` | Гильдии | ⬜ нет | ⬜ |

Перед каждой задачей — обновляем карту, явно указываем «портируем X из Y».

---

## Phase 3. Приоритеты портирования

### Срочные (закроют большие куски TO-DO):
1. **Proc-flag система** — `UnitAuraProcHandler.cpp` (~5000 строк). Закроет Overpower/Rune Strike/Sword and Board/Natural Reaction одним рывком.
2. **Группы / партия** — `Group.cpp`. Препрек для подземелий, лута на партию, шеринга XP.
3. **Гильдии** — `Guild.cpp` + GuildMgr. ⬜ 0/47 опкодов.
4. **Петы (хантер/локер)** — `Pet.cpp`, `PetAI.cpp`. ⬜ 0/28.

### Средний приоритет:
5. **Почта** — `Mail.cpp`, `MailMgr.cpp`. ⬜ 0/12.
6. **Социалка** — friend/ignore/whisper/channels. ⬜ 3/58.
7. **LFG** — `LFGMgr.cpp`. ⬜ 0/29.
8. **Аукцион** — `AuctionHouse.cpp`. ⬜ 0/14.

### Долгий хвост:
9. PvP (BG/Арена).
10. Инстансы + boss-скрипты (огромный объём ScriptDev2).
11. Достижения, календарь, транспорт.

Каждая система — отдельный мини-эпик в канбане «Порт CMaNGOS».

---

## Phase 4. Workflow портирования системы

Стандартный pipeline:

1. **Открыть CMaNGOS-исходник** в `\\homeserver\WowProject\repos\mangos-wotlk\src\…`.
2. **Прочитать целиком** .cpp + .h файлы, понять контракт.
3. **Обновить `docs/cmangos-port-map.md`**: новая строка.
4. **Написать C#-аналог**:
   - Имена классов/методов близкие к CMaNGOS (для diff'ов).
   - Header-комментарий со ссылкой на CMaNGOS.
   - Адаптация к нашим паттернам: async/await, DI, `WorldSession`/`Combat`/`Progression` вместо C++-Unit.
5. **Тестирование**: unit-тесты где возможно; integration-smoke в игре через `.cast`/`.learn`/манекен.
6. **Commit**: «port(cmangos): … from src/game/X/Y.cpp», ссылка на CMaNGOS commit.
7. **PR** (public репо — внешним contributors будет понятна логика).

---

## Phase 5. Что НЕ портируем (наша оригинальная инфраструктура)

| Компонент | Зачем сохраняем |
|---|---|
| Spell QA Harness (M12) | Авто-прогон спеллов, захват аномалий. В CMaNGOS такого нет. |
| Канбан-доска (БД `project`) | 1900+ регрессионных тикетов, метки, KB-эпики. |
| Web админка (Razor) | Регистрация/аккаунты/инвентарь через UI, дашборд готовности. |
| Регрессионный пайплайн | Аддон QA + tester_guid + class/race-привязка. |
| Dev-команды (`.cast`, `.learn`, …) | Уже работают; не ломаем UX. |
| `tools/MapExtractor`, `MmapGen`, `regression-import` | Data-инструменты, независимы от ядра. |
| CI/CD (GitHub Actions, runners) | .NET stack уже работает. |

---

## Phase 6. БД — никаких изменений

| БД | Что |
|---|---|
| `alexwow_auth` (EF Core schema) | Оставляем. Удобнее CMaNGOS-`realmd` (типизация, миграции). Если нужны их колонки — EF migration. |
| `mangos` (CMaNGOS dump) | Оставляем. Уже совместима. Обновления — SQL-стиль CMaNGOS. |
| `project` (канбан) | Оставляем. Не зависит от ядра. |

---

## Workflow

- **Источник CMaNGOS**: `\\homeserver\WowProject\repos\mangos-wotlk\` (read-only зеркало, fetch по cron на homeserver).
- **Карта портирования**: `docs/cmangos-port-map.md` — обновляется перед каждой задачей.
- **Канбан**: новый Project «Порт CMaNGOS» в `https://alexwow.home.srv/Board`, по одному эпику на крупную систему.
- **Коммиты**: формат `port(cmangos): <система> from src/game/X/Y.cpp`.

---

## Verification (после Phase 1)

1. **LICENSE / репо**: на GitHub видно «GPL-2.0», репо публичный, README содержит attribution к CMaNGOS.
2. **`docs/cmangos-port-map.md`**: открывается, видны строки соответствий, 5-10 уже-сделанных систем.
3. **Канбан**: появился Project «Порт CMaNGOS», 4-10 эпиков по приоритетам.
4. **Build/tests**: `dotnet build` + `dotnet test` — без регрессий.
5. **Прод**: ничего не меняется (это организационный pivot, не код).

После Phase 7 первая реальная задача — **Proc-flag система** (закроет Overpower/Rune Strike/Sword and Board/Natural Reaction).
