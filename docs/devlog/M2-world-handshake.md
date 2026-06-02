# Devlog — M2: World handshake

**Статус:** ✅ реализовано, развёрнуто, проверено живым клиентом.

## Цель вехи

World-сервер (`mangosd`, порт 8085): клиент проходит аутентификацию в мире и
устанавливает **шифрованную** сессию (без вылета).

## Что сделано

- **`AlexWoW.Cryptography`** (M2-часть):
  - `Arc4` — потоковый шифр RC4 (в BCL отсутствует);
  - `WorldHeaderCrypt` — шифрование заголовков: ключи RC4 = HMAC-SHA1(сид, session key),
    фиксированные сиды как в TrinityCore, прогрев 1024 байта;
  - `WorldAuth.ComputeAuthSessionDigest` — SHA1(account‖0‖clientSeed‖authSeed‖sessionKey).
- **`AlexWoW.WorldServer`** — TCP-листенер 8085, `WorldSession`:
  - `SMSG_AUTH_CHALLENGE` (открытый заголовок) с серверным authSeed;
  - приём `CMSG_AUTH_SESSION`, проверка digest по session key из auth-БД;
  - инициализация шифрования, `SMSG_AUTH_RESPONSE` (уже зашифрованный заголовок);
  - switch-диспетчер опкодов, `CMSG_PING` → `SMSG_PONG`.
- **Деплой**: отдельный рантайм-образ `Dockerfile.world`, сервис `alexwow-world` в compose,
  порт 8085. Деплой-скрипт публикует оба сервера (`publish/auth`, `publish/world`).

## Ключевые детали протокола (3.3.5a)

- Заголовки: **сервер→клиент** = uint16 size (BE) + uint16 opcode (LE);
  **клиент→сервер** = uint16 size (BE) + uint32 opcode (LE). `size` включает длину opcode.
- Шифруется **только заголовок** (RC4), тело — открытое.
- `SMSG_AUTH_CHALLENGE` и `CMSG_AUTH_SESSION` идут с **открытыми** заголовками;
  шифрование включается сразу после приёма `CMSG_AUTH_SESSION` (когда известен session key).
- Опкоды: `SMSG_AUTH_CHALLENGE=0x1EC`, `CMSG_AUTH_SESSION=0x1ED`, `SMSG_AUTH_RESPONSE=0x1EE`.

## Проверка

- ✅ `dotnet test` — 12/12 (вкл. **канонический тест-вектор RC4** — внешняя валидация шифра;
  round-trip header crypt; round-trip auth digest).
- ✅ Смоук-тест по TCP на сервере: `SMSG_AUTH_CHALLENGE` корректен — `size=42`,
  `opcode=0x1EC`, payload 40 байт, видны `01 00 00 00` и authSeed.
- ✅ **Проверено живым клиентом 3.3.5a** (2026-06-02): после "шифрование включено"
  клиент шлёт зашифрованные опкоды, которые расшифровываются корректно
  (`0x37 CMSG_CHAR_ENUM`, `0x38C CMSG_REALM_SPLIT`, `0x4FF`) → RC4 байт-в-байт.
  Клиент висит на "загрузке списка персонажей" (ждёт `SMSG_CHAR_ENUM` — это M3).

## Дальше → M3

Экран персонажей: БД `characters`, `CMSG_CHAR_ENUM` → `SMSG_CHAR_ENUM`,
создание/удаление персонажа, минимальный парсинг DBC (расы/классы).
