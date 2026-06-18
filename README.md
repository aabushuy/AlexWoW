# AlexWoW

[![CI](https://github.com/aabushuy/AlexWoW/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/aabushuy/AlexWoW/actions/workflows/ci.yml)

Самописный сервер **World of Warcraft** на **.NET 10** + **MySQL**.
Цель — играбельный сервер под **WotLK 3.3.5a (build 12340)**. Эталон — CMaNGOS-WotLK.

**Новый разработчик?** Смотри `\\homeserver\WowProject\dev\START_HERE.md` (SMB) или клон + `docs/onboarding/`.

**Статус:** ✅ M1–M5 + большая часть M6 — **проверено живым клиентом 3.3.5a**.
Готово: логин, экран персонажей, вход в мир, движение/чат (с кириллицей), видимость
NPC/объектов/игроков, рельеф+коллизии+навмеш (maps/vmaps/mmaps), стартовая экипировка
и инвентарь, вендор, бой и спеллы, лут, ИИ существ; прогрессия (XP/статы/тренеры/таланты, M9),
спелл-система на данных (M10). 🟡 В работе — техдолг/баги (M7) и профессии (M9.5). Готова
**веб-панель игрока** (M8: регистрация/вход/смена пароля/персонажи) — `https://alexwow.home.srv`.
Подробности и статусы — в [дорожной карте](docs/roadmap.md); сводный дашборд готовности
(домены/скорость, ~58% взвешенно) — в [статусе проекта](docs/status.md).

## Документация

Вся документация — в [`docs/`](docs/README.md):

- [Архитектура](docs/architecture.md) — компоненты (auth/world/web), протокол 3.3.5a, данные, стек, эталоны
- [Дорожная карта](docs/roadmap.md) — вехи M0–M10 + веб-панель M8 со статусами
- [Быстрый старт](docs/getting-started.md) — сборка, тесты, запуск, подключение клиента
- [Деплой](docs/deployment.md) — выкладка на домашний сервер; веб-панель за Caddy
- [Дневник разработки](docs/devlog/) — журнал по вехам (вкл. [M8 веб-интерфейс](docs/devlog/M8-web-interface.md))

## TL;DR

```bash
docker compose up -d alexwow-mysql                                   # БД
dotnet run --project src/AlexWoW.AuthServer -- create-account test test
dotnet run --project src/AlexWoW.AuthServer                          # логин-сервер :3724
dotnet test                                                         # тесты SRP6
```

Деплой на домашний сервер — `./deploy/deploy.ps1` (сборка локально, на сервер едут бинарники).

> ⚠️ Данные клиента (DBC/maps/...) в репозиторий не коммитятся.
