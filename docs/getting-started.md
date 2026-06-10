# Быстрый старт (локальная разработка)

## Требования

- **.NET 10 SDK**
- **MySQL** (проще всего поднять контейнер из compose)
- (для проверки клиентом) **WoW 3.3.5a, build 12340**

## Структура решения

```
src/
  AlexWoW.Common         — бинарные примитивы пакетов (ByteReader/Writer)
  AlexWoW.Cryptography   — SRP6 (сервер/клиент), session key, RC4 header crypt
  AlexWoW.Database       — доступ к MySQL (Dapper): auth, characters, world
  AlexWoW.DataStores     — загрузка клиентских данных (maps/vmaps/mmaps), DBC
  AlexWoW.AuthServer     — логин-сервер (exe) + CLI (create-account, reset-all-passwords)
  AlexWoW.WorldServer    — world-сервер (exe): сессии, мир, бой, спеллы, квесты
  AlexWoW.Web            — веб-панель игрока (ASP.NET Core Razor Pages, M8): регистрация/вход/персонажи
tools/
  MapExtractor           — .NET-экстрактор DBC/maps/vmaps из клиента (MPQ)
  MmapGen                — генератор навмеша (mmaps) на DotRecast
  scripts/               — SQL-хелперы и сгенерированные дампы (дампы — в .gitignore)
tests/
  AlexWoW.Cryptography.Tests — round-trip тесты SRP6
  extractor-output/      — локальные пробные выгрузки экстрактора (в .gitignore)
data/                    — локальные хранимые данные (out-maps и т.п.; в .gitignore)
```

## Поднять MySQL

```bash
docker compose up -d alexwow-mysql
```

Строка подключения настраивается в `src/AlexWoW.AuthServer/appsettings.json`
(секция `AuthServer:ConnectionString`) или через переменную окружения
`AuthServer__ConnectionString`.

## Создать аккаунт и запустить сервер

```bash
# Создать тестовый аккаунт (генерирует SRP6 соль + верификатор)
dotnet run --project src/AlexWoW.AuthServer -- create-account test test

# Запустить логин-сервер (слушает 3724)
dotnet run --project src/AlexWoW.AuthServer
```

## Веб-панель игрока (локально)

```bash
# Строка подключения — секция Web:ConnectionString в src/AlexWoW.Web/appsettings.json
# или переменная окружения Web__ConnectionString
dotnet run --project src/AlexWoW.Web
```

Открой выведенный `http://localhost:<порт>`. Регистрация просит **email** (вход на сайт) и
**имя аккаунта** (вход в игру — латиница/цифры, без «@»). Миграции БД применяет AuthServer
(панель только читает/пишет). На проде панель отдаётся через Caddy по `https://alexwow.home.srv`
(см. [deployment.md](deployment.md)).

## Тесты

```bash
dotnet test
```

## Подключение клиента (Windows-машина)

1. Клиент строго **WoW 3.3.5a (build 12340)**.
2. В файле `<WoW>/WTF/Config.wtf` (или `realmlist.wtf`) укажи адрес логин-сервера:
   ```
   set realmlist "192.168.2.210"
   ```
   (для локального сервера — `127.0.0.1`)
3. Запусти `Wow.exe`, войди под созданным аккаунтом → должен появиться список реалмов.

> ⚠️ Данные клиента (DBC/maps/...) в репозиторий не коммитятся — они извлекаются
> из твоей копии клиента на вехах M3+.
