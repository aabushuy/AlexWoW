# AlexWoW

[![CI](https://github.com/aabushuy/AlexWoW/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/aabushuy/AlexWoW/actions/workflows/ci.yml)

**World of Warcraft 3.3.5a (WotLK, build 12340) server — C# port of [CMaNGOS-WoTLK](https://github.com/cmangos/mangos-wotlk)** + собственный пласт инфраструктуры (Web-админка, QA-харнес, канбан-доска регрессии).

Стэк: **.NET 10**, **MySQL 8**, EF Core (auth) + Dapper (world). ASP.NET Core Razor Pages для веб-панели. Навмеш на [DotRecast](https://github.com/ikpil/DotRecast).

> 🔄 **Стратегический разворот.** Проект идёт по пути «лоб-в-лоб» порта CMaNGOS-WoTLK на C#: переносим систему за системой с явной ссылкой на исходник. Полное обоснование и фазы — в [docs/strategy/cmangos-port.md](docs/strategy/cmangos-port.md).

## Статус

- ✅ Ядро проверено живым клиентом 3.3.5a: логин, экран персонажей, вход в мир, движение/чат (с кириллицей), maps/vmaps/mmaps, бой и спеллы, лут, ИИ существ, прогрессия (XP/статы/таланты), профессии.
- 🟡 В работе: техдолг, гильдии, группы/рейды, петы, PvP, инстансы — крупные XL-домены.
- ⬜ Контентный «хвост»: социалка, аукцион, почта, LFG, достижения.

Прогресс по доменам ведётся на канбан-доске (БД `project`) — внутренний дашборд `alexwow.home.srv/dashboard`.

## Что в репо

| Каталог | Что |
|---|---|
| `src/AlexWoW.AuthServer` | Логин-сервер (SRP6, реалм-листинг) |
| `src/AlexWoW.WorldServer` | World-сервер (опкоды, сессии, бой, спеллы, ИИ, квесты) |
| `src/AlexWoW.Web` | Веб-панель игрока + админка (ASP.NET Core Razor) + REST API канбан-доски |
| `src/AlexWoW.{Common,Cryptography,Database,DataStores}` | Общие библиотеки: бинарный протокол, SRP6/RC4, EF Core + Dapper, DBC/maps |
| `tools/MapExtractor` | Экстрактор DBC/maps/vmaps/иконок из клиента (MPQ) |
| `tools/MmapGen` | Генератор навмеша (mmaps) на DotRecast |
| `tools/addons/AlexQATester` | WoW-аддон для QA-харнеса (захват аномалий спеллов) |
| `tools/regression-import` | Импорт регрессионных тикетов из CMaNGOS-баг-репортов |
| `deploy/` | docker-compose, deploy-скрипты, секреты-шаблоны |
| `docs/` | Архитектура, статус, стратегия, code-style, onboarding |

## Быстрый старт

См. [docs/getting-started.md](docs/getting-started.md). TL;DR:

```bash
# 1. БД
docker compose up -d alexwow-mysql

# 2. Аккаунт
dotnet run --project src/AlexWoW.AuthServer -- create-account test test

# 3. Логин-сервер (:3724)
dotnet run --project src/AlexWoW.AuthServer

# 4. World-сервер (:8085)
dotnet run --project src/AlexWoW.WorldServer

# 5. Веб-панель
dotnet run --project src/AlexWoW.Web

# Тесты
dotnet test
```

Клиент: WoW 3.3.5a, build 12340. `WTF/Config.wtf` → `set realmlist "127.0.0.1"`.

⚠️ Данные клиента (DBC/maps/vmaps/mmaps, *.MPQ) **в репозиторий не коммитятся** — extract'аются из вашей копии клиента через `tools/MapExtractor`.

## Документация

- [Архитектура](docs/architecture.md) — компоненты, сетевой протокол 3.3.5a, БД, эталонные исходники
- [Стратегия порта CMaNGOS](docs/strategy/cmangos-port.md) — фазы и принципы
- [Быстрый старт](docs/getting-started.md) — локальная сборка/запуск
- [Деплой](docs/deployment.md) — продовая выкладка
- [Code style](docs/code-style.md) — конвенции C# в репо
- [Self-hosted runners](deploy/RUNNERS.md), [Секреты](deploy/SECRETS.md) — инфраструктура

## Лицензия

GPL-2.0 (планируется в Phase 1 — добавление файла `LICENSE` и атрибуции `NOTICE.md`). Порт CMaNGOS-WoTLK считается derivative work, поэтому совместимость с upstream-лицензией обязательна.

## Upstream / эталоны

| Источник | Назначение |
|---|---|
| [CMaNGOS-WoTLK](https://github.com/cmangos/mangos-wotlk) | **Главный эталон** для портирования (3.3.5a, GPL-2.0) |
| [TrinityCore 3.3.5](https://github.com/TrinityCore/TrinityCore/tree/3.3.5) | Альтернативный эталон для cross-check |
| [WCell](https://github.com/WCell/WCell-WoW) | Архивный C#-сервер 3.3.5 (для подсказок по C#-идиомам) |
| [DotRecast](https://github.com/ikpil/DotRecast) | Навмеш (.NET-порт Recast/Detour) |
| [Foole.Mpq](http://github.com/Foole/MpqReader) | MPQ-парсер в `tools/MapExtractor/Foole.Mpq` (MIT) |

Соответствие «CMaNGOS-файл → наш порт» поддерживается в [docs/cmangos-port-map.md](docs/cmangos-port-map.md) (заводится в Phase 2).
