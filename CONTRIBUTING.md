# Contributing to AlexWoW

Спасибо за интерес. Этот документ описывает поток PR, стиль кода и коммитов.

## TL;DR

1. Issue → план → ветка → PR.
2. Стиль кода — [docs/code-style.md](docs/code-style.md), машинная часть — `.editorconfig`. Перед коммитом: `dotnet format` (или включи в IDE).
3. Один логический PR — один эпик. Не смешиваем рефакторинг и фичу.
4. CI зелёный = обязательное условие мерджа.
5. **Если портируешь систему из CMaNGOS** — header-комментарий со ссылкой на исходник, см. [docs/strategy/cmangos-port.md](docs/strategy/cmangos-port.md).

## Перед началом работы

- Прочти [docs/strategy/cmangos-port.md](docs/strategy/cmangos-port.md) — стратегический разворот, источник истины для портирования. Особенно если работаешь над крупной системой.
- Прогресс по доменам ведётся на канбан-доске (БД `project`, веб-дашборд). Загляни туда, чтобы понимать что уже сделано.
- Если задача крупная (новый домен) — заведи issue с описанием подхода до начала кодинга. Так мы сверим вектор и не задвоим работу.

## Поток PR

1. **Fork → ветка.** Имя ветки человекочитаемое: `port/guilds`, `fix/spell-1234-overpower`, `docs/contributing`.
2. **Коммитим маленькими шагами.** Каждый коммит — атомарная правка, проходит CI самостоятельно.
3. **PR из ветки в `main`.** Заполни описание: что меняем, зачем, как проверял. Линкуй issue (`Closes #123`).
4. **CI обязан быть зелёным** (build + tests + format). См. `.github/workflows/ci.yml`.
5. **Code review.** Принимаем замечания через ответные коммиты, не амендим уже отрецензированные.
6. **Merge:** squash merge с человеко-читаемым заголовком (формат коммитов ниже).

## Формат коммитов

Conventional-style, **scoped**, на русском:

```
<type>(<scope>): <короткое описание>

[опционально: тело — почему, какие тонкости]
```

Типы:
- `feat` — новая фича/опкод/механика
- `fix` — баг-фикс
- `port(cmangos)` — порт системы из CMaNGOS, в скобках scope = подсистема
- `refactor` — рефакторинг без поведенческих изменений
- `docs` — только документация
- `tools` — изменения в `tools/`
- `chore`, `build`, `ci` — инфраструктура

Scope — модуль/файл: `combat`, `spells`, `auth`, `web`, `addon`, `kanban`, `infra`.

Примеры:
- `feat(combat): AURA_STATE_DEFENSE для Revenge (kanban #3797)`
- `port(cmangos): Group.cpp + GroupMgr → src/AlexWoW.WorldServer/Handlers/Group/`
- `fix(addon): U+00B7 middle dot → '-' в Q-протоколе QA-аддона`

При портировании из CMaNGOS — линкуй исходник в теле коммита:

```
port(cmangos): Proc-flag система из UnitAuraProcHandler.cpp

См. https://github.com/cmangos/mangos-wotlk/blob/master/src/game/Spells/UnitAuraProcHandler.cpp
Закрывает: Overpower, Rune Strike, Sword and Board, Natural Reaction.
```

## Code style

Полный документ — [docs/code-style.md](docs/code-style.md). Ключевое:

- **Язык.** Комментарии и XML-доки — на русском. Технические термины (opcode, aura, GUID) не переводим.
- **C#.** PascalCase, `_camelCase` для приватных полей, file-scoped namespaces, `var` везде, отступ 4, строка ≤ 120, Allman.
- **Async.** Суффикс `Async`, `CancellationToken ct` последним.
- **SOLID + KISS + DRY.** Без god-классов и преждевременных абстракций.
- **Серилог.** Структурные параметры в шаблоне, не конкатенация.

`dotnet format` перед коммитом обязателен — CI проверяет `--verify-no-changes`.

## Тестирование

- Unit-тесты для чистой логики (криптография, парсеры, бой-калькуляции) — `tests/AlexWoW.*.Tests`.
- Integration через web-host — `tests/AlexWoW.Web.Tests`.
- Интерактивная проверка протокольных вещей — реальный клиент 3.3.5a (build 12340), dev-команды `.cast`, `.learn`, `.tele`.

Минимум: новый код покрыт там, где это естественно (логика без I/O). Регрессия по существующим системам не падает.

## Что особенно ценим

- **Точные порты CMaNGOS** с явной ссылкой на исходник в шапке файла. Это упрощает будущие диффы.
- **Маленькие PR** (≤ 400 строк). Большие PR разбиваем по эпикам.
- **Тесты** на новую логику.

## Что НЕ принимаем без обсуждения

- Массовый рефакторинг без согласованного плана.
- Смена технологического стека (БД, фреймворк, navmesh-движок).
- Сторонние зависимости без обоснования (минимизируем NuGet-зоопарк).
- Бинарники клиента в git (DBC/MPQ/maps — всегда генерируются локально через `tools/MapExtractor`).

## Лицензия и upstream

Проект под **GPL-2.0** (см. `LICENSE` — будет добавлен в Phase 1). Контрибьюции принимаются на тех же условиях. При портировании файла из CMaNGOS-WoTLK сохраняй атрибуцию в header-комментарии:

```csharp
// Порт CMaNGOS-WoTLK: src/game/Spells/SpellMgr.cpp
// (https://github.com/cmangos/mangos-wotlk). GPL-2.0.
```

## Контакты

- Issues / Discussions на GitHub.
- Безопасностные находки — приватно через GitHub Security Advisories (репо → Security → Advisories).
