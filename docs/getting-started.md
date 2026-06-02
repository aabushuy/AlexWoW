# Быстрый старт (локальная разработка)

## Требования

- **.NET 10 SDK**
- **MySQL** (проще всего поднять контейнер из compose)
- (для проверки клиентом) **WoW 3.3.5a, build 12340**

## Структура решения

```
src/
  AlexWoW.Common         — бинарные примитивы пакетов (ByteReader/Writer)
  AlexWoW.Cryptography   — SRP6 (сервер/клиент), session key
  AlexWoW.Database       — доступ к MySQL (Dapper), схема auth
  AlexWoW.AuthServer     — логин-сервер (exe) + CLI создания аккаунтов
tests/
  AlexWoW.Cryptography.Tests — round-trip тесты SRP6
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
