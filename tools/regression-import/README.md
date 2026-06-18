# Импорт регрессионных тикетов на абилки

Генератор тикетов для проекта **Регрессия абилок** на канбан-доске `https://alexwow.home.srv/Board`.

## Что делает

Читает `mangos.spell_template` JOIN `mangos.npc_trainer` на homeserver, группирует по `SpellName`/`SpellFamilyName`, оставляет **высший ранг** (`MAX(SpellLevel)`) каждой абилки и создаёт по тикету на каждую.

Тикеты помечены меткой `regression` — авто-архивация ([KanbanArchiveBackgroundService](../../src/AlexWoW.Web/Services/Kanban/KanbanArchiveBackgroundService.cs)) их **не трогает**, можно ручно возвращать из Done в Backlog через UI карточки для повторного прогона.

## Идемпотентность

Перед POST'ом скрипт собирает все уже заведённые regression-тикеты по этому проекту (включая архивные) и парсит `spell_id` из заголовка по regex `^#(\d+) ·`. Уже заведённые пропускает. Безопасно перезапускать.

## Запуск

Требования: Python 3.10+, доступ по `ssh homeserver`. **Никаких pip-зависимостей не нужно** — всё через `ssh` + `curl` + `docker exec mysql`.

```bash
# Печатает первые 5 тикетов в JSON, ничего не пушит
python tools/regression-import/generate.py --dry-run --limit 5

# Реально создаёт 5 тикетов (для проверки на проде)
python tools/regression-import/generate.py --limit 5

# Полный прогон
python tools/regression-import/generate.py

# Только один класс — Маг (SpellFamilyName=3)
python tools/regression-import/generate.py --family 3
```

## Файлы

| Файл | Назначение |
|---|---|
| `generate.py` | Основной скрипт (CLI). |
| `template.py` | Шаблоны: `build_ticket(row, epic_id)` + рендеры title/test_steps/expected_result. |
| `epics.json` | ID проекта и эпиков на доске (создаются один раз в Phase B). |

## SpellFamilyName → класс

| Code | Класс | Эпик-метка |
|---|---|---|
| 0 | Общие/расовые | generic |
| 3 | Маг | mage |
| 4 | Воин | warrior |
| 5 | Чернокнижник | warlock |
| 6 | Жрец | priest |
| 7 | Друид | druid |
| 8 | Разбойник | rogue |
| 9 | Охотник | hunter |
| 10 | Паладин | paladin |
| 11 | Шаман | shaman |
| 15 | Рыцарь смерти | deathknight |

## Возврат регрессии в работу

Через UI карточки `/Ticket`: меняем Status → `Backlog`, нажимаем «Сохранить». `KanbanRepository.UpdateAsync` сам сбросит `done_at` и `is_archive` (выход из Done). Тестер увидит обновлённую задачу в аддоне после следующего `qatasks`.

## Куда смотреть, если что-то сломалось

- POST вернул HTTP-ошибку → проверь `WEB_API_TOKEN` в `/data/docker/alexwow-config/.env` (на homeserver) совпадает с тем, что подхватил `alexwow-web`.
- SQL не возвращает строк → проверь `mangos.npc_trainer` не пустой: `ssh homeserver "docker exec -i alexwow-mysql mysql -ualexwow -palexwow -e 'SELECT COUNT(*) FROM mangos.npc_trainer;'"`.
- Дубли → проверь, что у уже-заведённых тикетов в title формат `#<spell_id> ·` (regex чувствителен к точке-разделителю).
