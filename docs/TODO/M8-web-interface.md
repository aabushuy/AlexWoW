# M8 — Веб-интерфейс игрока (эпик)

**Цель:** сайт для игроков: регистрация аккаунта, смена пароля, просмотр информации о персонажах.
Позже — смена расы/пола, покупка игрового золота.

**Стек:** **ASP.NET Core** (`AlexWoW.Web`, .NET 10, Razor Pages) — в стеке проекта; переиспользует
`AlexWoW.Cryptography` (SRP6) и `AlexWoW.Database` (`AuthDatabase`, `CharactersDatabase`).
**Деплой:** контейнер за **Caddy** на homeserver (как Vikunja), домен напр. `alexwow.home.srv`;
рантайм-only Dockerfile (COPY publish), `deploy.ps1`.

**Данные:** одна БД `alexwow_auth` (таблицы `account` + `characters`). Регистрация = создание
аккаунта SRP6 (salt+verifier из `Srp6.CalculateVerifier(username, password, salt)`), как в CLI
`AuthServer create-account` (`AccountCreator.cs`). Проверка пароля в панели = пересчёт verifier по
сохранённому salt и сравнение со `account.verifier`.

**Решения с пользователем:**
- Логин = **email-адрес**. Регистрация простая: email, пароль, подтверждение пароля.
- **Без подтверждения по почте** (пока).

⚠️ **Важно:** `account.username VARCHAR(32)` мала для email — расширить до `VARCHAR(255)` (миграция).
Имя аккаунта = email в верхнем регистре (как требует SRP6/клиент). Клиент логинится этим email.

---

## Срезы

### M8.1 — Каркас веб-приложения
Проект `AlexWoW.Web` (ASP.NET Core Razor Pages), DI к `AuthDatabase`/`CharactersDatabase` (строка
подключения через конфиг/env), базовый layout/тема, главная страница, health-check.
Деплой: `Dockerfile.web` (runtime-only), сервис в `docker-compose.yml` в сети `frontend` за Caddy,
домен + запись в Caddyfile. Критерий: сайт открывается по домену.

### M8.2 — Регистрация аккаунта
Форма: email (логин), пароль, подтверждение. Валидация: формат email, длина пароля, совпадение
подтверждения, уникальность (`AccountExistsAsync`). Создание: `Srp6.CalculateVerifier` → salt+verifier
→ `CreateAccountAsync` (username = email.ToUpper()). Миграция `account.username` → `VARCHAR(255)`.
Без email-верификации. Критерий: зарегистрировался на сайте → этим email+паролем входит в игру.

### M8.3 — Вход в веб-панель + сессии
Форма входа (email+пароль): проверка пересчётом verifier по salt и сравнением. Cookie-аутентификация
(ASP.NET Core), `account_id` в сессии, logout, защита страниц панели. Критерий: вход/выход работают,
приватные страницы недоступны без входа.

### M8.4 — Смена пароля
Под аутентификацией: текущий пароль (проверка) + новый + подтверждение → новый salt+verifier →
`UpdatePasswordAsync` (добавить в `AuthDatabase`). Критерий: сменил пароль на сайте → новый пароль
работает в игре, старый — нет.

### M8.5 — Просмотр персонажей
Список персонажей аккаунта (`GetByAccountAsync` по `account_id`) + детали: имя, раса/класс/уровень,
зона/карта, деньги, экипировка (read-only). Критерий: вижу свои персонажи и их данные.

### M8.6 — Смена расы/пола (позже)
Правка `characters` (race/gender) + флаг кастомизации/at-login, чтобы клиент применил при входе
(CHAR_ENUM customization flag / `AT_LOGIN_*`). Возможна плата (см. M8.7).

### M8.7 — Покупка игрового золота (позже)
Платёжный флоу (провайдер/заглушка) → начисление `characters.money` (+ аудит транзакций,
античит/лимиты). Безопасность платежей.

---

## Безопасность / заметки
- Пароли не хранятся (только SRP6 salt+verifier) — как в игре.
- Веб-сессия маппится на `account_id`; данные персонажей фильтруются по нему.
- HTTPS через Caddy (self-signed CA на LAN).
- Rate-limit на регистрацию/вход — позже (антибрут).
