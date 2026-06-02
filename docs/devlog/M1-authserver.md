# Devlog — M1: AuthServer (логин-сервер)

**Статус:** ✅ реализовано, развёрнуто. Осталась финальная проверка живым клиентом.

## Цель вехи

Логин-сервер (`realmd`), чтобы клиент 3.3.5a доходил до экрана выбора реалма:
SRP6-аутентификация + список реалмов + хранение аккаунтов.

## Что сделано

- **`AlexWoW.Cryptography`** — SRP6 по алгоритму CMaNGOS/TrinityCore:
  - `Srp6` — константы (N, g=7, k=3), helpers (LE-байты, SHA1, H-Interleave для session key);
  - `Srp6Server` — challenge (B) и проверка proof (M1) → session key + M2;
  - `Srp6Client` — клиентская сторона для тестов.
- **`AlexWoW.Common`** — `ByteReader`/`ByteWriter` (little-endian примитивы пакетов).
- **`AlexWoW.Database`** — Dapper + MySqlConnector, схема `account` + `realmlist`, сид реалма.
- **`AlexWoW.AuthServer`** — TCP-листенер на 3724, сессия `challenge → proof → realmlist`,
  CLI `create-account`, хостинг через `Microsoft.Extensions.Hosting` + Serilog.
- **Тесты** — 6 round-trip тестов SRP6 (верный/неверный пароль, длина ключа, детерминизм).

## Принятые решения

- **SRP6 строго по эталону.** k=3 (legacy SRP-6, не 6a), все большие числа little-endian,
  session key через H-Interleave с отбрасыванием старших нулей по нечётным позициям —
  иначе клиент не сойдётся по M1.
- **Пароль не храним** — только соль `s` и верификатор `v = g^x mod N`.
- **MySQL вместо Postgres** — чтобы грузить дампы мира эталонов без конвертации.
- **Деплой: локальная сборка → бинарники на сервер** (рантайм-only Docker-образ).

## Грабли

- **Гонка старта с MySQL.** Auth-контейнер стартовал раньше готовности БД и падал
  (перезапускался). Починено: `EnsureSchema` повторяется с задержкой (до 30 попыток).
- **Кодировка PowerShell.** WPS 5.1 читает `.ps1` без BOM как ANSI → кириллица в
  комментариях ломала парсинг. Скрипты деплоя держим ASCII-only.

## Проверка

- ✅ `dotnet test` — 6/6 проходят (внутренняя корректность SRP6: клиент и сервер
  независимо выводят одинаковый session key).
- ✅ Развёрнуто на homeserver: порт 3724 слушается, аккаунт `test`/`test` создан,
  переживает пересборку (named-volume).
- ⬜ **Финальная валидация — живой клиент 3.3.5a.** Внутренние тесты подтверждают
  математику, но байт-в-байт совместимость SRP6 покажет только `Wow.exe`
  (экран выбора реалма). Делается после скачивания клиента.

## Дальше → M2

World handshake: `AlexWoW.WorldServer` (порт 8085), фрейминг world-пакетов,
**RC4 header crypt** (HMAC-SHA1 из session key), `CMSG_AUTH_SESSION` →
`SMSG_AUTH_RESPONSE`, диспетчер опкодов.
