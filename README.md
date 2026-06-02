# AlexWoW

Самописный сервер **World of Warcraft** на **.NET 10** + **MySQL**.
Цель — играбельный сервер под **WotLK 3.3.5a (build 12340)**. Эталон — CMaNGOS-WotLK.

**Статус:** ✅ M1 — логин-сервер (`realmd`): SRP6-аутентификация + список реалмов.
Клиент доходит до экрана выбора реалма. Следующая веха — M2 (вход в мир).

## Документация

Вся документация — в [`docs/`](docs/README.md):

- [Архитектура](docs/architecture.md) — компоненты, протокол 3.3.5a, данные, стек, эталоны
- [Дорожная карта](docs/roadmap.md) — вехи M0–M6 со статусами
- [Быстрый старт](docs/getting-started.md) — сборка, тесты, запуск, подключение клиента
- [Деплой](docs/deployment.md) — выкладка на домашний сервер
- [Дневник разработки](docs/devlog/) — журнал по вехам

## TL;DR

```bash
docker compose up -d alexwow-mysql                                   # БД
dotnet run --project src/AlexWoW.AuthServer -- create-account test test
dotnet run --project src/AlexWoW.AuthServer                          # логин-сервер :3724
dotnet test                                                         # тесты SRP6
```

Деплой на домашний сервер — `./deploy/deploy.ps1` (сборка локально, на сервер едут бинарники).

> ⚠️ Данные клиента (DBC/maps/...) в репозиторий не коммитятся.
