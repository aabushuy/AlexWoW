# AlexWoW — Project Context for AI Assistants

## Что это

Самописный сервер World of Warcraft на .NET 10 + MySQL.
Клиент: WotLK 3.3.5a (build 12340). Эталон: CMaNGOS-WotLK.

## Структура

- `src/` — AuthServer, WorldServer, Web, Common, Cryptography, Database, DataStores
- `tests/` — тесты
- `deploy/` — docker-compose, SQL, deploy.ps1
- `docs/` — архитектура, статус
- `docs/archive/` — ⚠️ **НЕ АКТУАЛЬНА**, устаревший контент, не использовать как источник истины

## Трекинг задач

Задачи по проекту ведутся в **БД `project`** на домашнем сервере (MySQL на `home.srv` / `192.168.2.210`).

## Инфраструктура / Homeserver

Справка: `D:\Seafile\Home Library\Knowledge Base\20 IT\10 Homeserver.md`
SSH: `ssh homeserver` (user alex, passwordless sudo)
Docker Compose: `/data/docker/docker-compose.yml`
AlexWoW контейнеры: `/data/docker/alexwow/`

## Важные замечания

- Данные клиента (DBC/maps/vmaps/mmaps) в репозиторий не коммитятся.
- Язык проекта: C# / .NET 10, комментарии на русском.
- Code style: см. `docs/code-style.md`.
