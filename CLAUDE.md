# AlexWoW — Project Context for AI Assistants

## Что это

Самописный сервер World of Warcraft на .NET 10 + MySQL.
Клиент: WotLK 3.3.5a (build 12340). Эталон: CMaNGOS-WotLK.

## Структура

- `src/` — AuthServer, WorldServer, Web, Common, Cryptography, Database, DataStores
- `tests/` — тесты
- `deploy/` — docker-compose, SQL, deploy.ps1
- `docs/` — архитектура, стратегия порта CMaNGOS, code-style, onboarding

## Трекинг задач

Задачи по проекту ведутся в **БД `project`** на домашнем сервере (MySQL на `home.srv` / `192.168.2.210`).

## Аддоны

Клиентские аддоны (WoW 3.3.5a, Lua) — в `tools/addons/`. Настройка разработки и тестирования
(в т.ч. проверки **без запуска клиента**: `pwsh tools/addons/run-checks.ps1`) — в
`docs/onboarding/addon-development.md`. Все задачи по аддонам — в эпике **Addons** (канбан, project 41).

## Инфраструктура / Homeserver

Справка: `D:\Seafile\Home Library\Knowledge Base\20 IT\10 Homeserver.md`
SSH: `ssh homeserver` (user alex, passwordless sudo)
Docker Compose: `/data/docker/docker-compose.yml`
AlexWoW контейнеры: `/data/docker/alexwow/`

## Важные замечания

- Данные клиента (DBC/maps/vmaps/mmaps) в репозиторий не коммитятся.
- Язык проекта: C# / .NET 10, комментарии на русском.
- Code style: см. `docs/code-style.md`.

## External resources (shared storage)

SMB-шара `\\homeserver\WowProject` (юзер `wowshare`) содержит:
- `repos/` — read-only зеркала `mangos-wotlk` и `TrinityCore` (fetch по cron'у)
- `client/WoW335/` — клиент 3.3.5a с аддонами и `realmlist.wtf`
- `claude/` — shared промпты/инструкции для AI-агентов
- `dev/START_HERE.md` — onboarding для нового разработчика

Конфиги (sample): `docs/onboarding/{ssh-config.sample,mcp-config.sample.json,runner-setup.md}` — source of truth в репо, на шаре — копии.

## CI/CD

- Деплой: автоматический при push в `main` через `.github/workflows/ci.yml` (build → test → deploy-test на homeserver).
- Emergency-fallback: `deploy/deploy-manual.ps1`.
- Секреты: `/data/docker/alexwow-config/.env` на homeserver, см. `deploy/SECRETS.md`.
- Self-hosted runners: `deploy/RUNNERS.md` + `docs/onboarding/runner-setup.md`.
