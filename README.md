# AlexWoW

Самописный сервер World of Warcraft на **.NET 10** + **MySQL**.
Цель: играбельный сервер под **WotLK 3.3.5a (build 12340)**. Эталон — CMaNGOS-WotLK.

Полный архитектурный разбор: [ANALYSIS.md](ANALYSIS.md).

## Текущий статус — веха M1 (AuthServer)

Реализован логин-сервер (`realmd`):
- SRP6-аутентификация (challenge → proof);
- список реалмов;
- хранение аккаунтов в MySQL (соль + верификатор, пароль не хранится).

Этого достаточно, чтобы клиент 3.3.5a дошёл до **экрана выбора реалма**.

## Структура

```
src/
  AlexWoW.Common         — бинарные примитивы пакетов (ByteReader/Writer)
  AlexWoW.Cryptography   — SRP6 (сервер/клиент), session key
  AlexWoW.Database       — доступ к MySQL (Dapper), схема auth
  AlexWoW.AuthServer     — логин-сервер (exe) + CLI создания аккаунтов
tests/
  AlexWoW.Cryptography.Tests — round-trip тесты SRP6
```

## Локальный запуск (для разработки)

Нужен MySQL. Быстрее всего поднять только БД из compose:

```bash
docker compose up -d alexwow-mysql
```

Настрой строку подключения в `src/AlexWoW.AuthServer/appsettings.json`, затем:

```bash
# Создать тестовый аккаунт
dotnet run --project src/AlexWoW.AuthServer -- create-account test test

# Запустить логин-сервер
dotnet run --project src/AlexWoW.AuthServer
```

Тесты:

```bash
dotnet test
```

## Деплой на домашний сервер (homeserver, 192.168.2.210)

**Сборка локальная**, на сервер копируются только готовые бинарники — на сервере
нет SDK/restore/компиляции (рантайм-образ просто копирует `./publish`).

```powershell
# с Windows-машины, из корня репозитория
./deploy/deploy.ps1
```

Скрипт делает: `dotnet publish -c Release` → `scp publish + Dockerfile + compose`
на сервер → `docker compose up -d --build` (build = тривиальный COPY).

```bash
# создать аккаунт внутри контейнера (на сервере)
docker compose exec alexwow-auth dotnet AlexWoW.AuthServer.dll create-account test test
```

Порт `3724/tcp` (логин) проброшен на хост. World-сервер (порт 8085) появится на следующей вехе.

## Подключение клиента (Windows-машина)

1. Клиент строго **WoW 3.3.5a (build 12340)**.
2. В файле `<WoW>/WTF/Config.wtf` (или `realmlist.wtf`) укажи адрес логин-сервера:
   ```
   set realmlist "192.168.2.210"
   ```
3. Запусти `Wow.exe`, войди под созданным аккаунтом → должен появиться список реалмов.

> ⚠️ Данные клиента (DBC/maps/...) в репозиторий не коммитятся — они извлекаются из твоей копии клиента на следующих вехах.
