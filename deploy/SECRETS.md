# Secrets

Все секреты лежат на homeserver в `/data/docker/alexwow-config/.env`.

**Путь намеренно ВНЕ `/data/docker/alexwow/`** — потому что `deploy-manual.ps1` (и CI) делают `rm -rf /data/docker/alexwow`, что стёрло бы секреты, если бы они лежали внутри.

## Ключи

| Ключ                       | Назначение                                                                                          | Где ещё нужен                           |
|----------------------------|-----------------------------------------------------------------------------------------------------|------------------------------------------|
| `MYSQL_ROOT_PASSWORD`      | Пароль root для `alexwow-mysql`.                                                                    | Не нужен снаружи.                        |
| `MYSQL_USER` / `MYSQL_PASSWORD` | Юзер БД для приложений (auth/world/web).                                                       | Connection strings в compose.            |
| `REALM_ADDRESS`            | IP, который AuthServer отдаёт клиенту как адрес world (`192.168.2.210`). Не секрет.                | —                                        |
| `WEB_API_TOKEN`            | `X-Api-Token` для REST API канбана (`/api/kanban/*`, KB5). Нужен MCP-клиенту (`.mcp.json kanban`). | `.mcp.json` на dev-машинах.              |

## Запуск compose

```bash
docker compose --env-file /data/docker/alexwow-config/.env -f /data/docker/alexwow/docker-compose.yml up -d --build
```

Без `--env-file` compose попробует найти `.env` рядом с `docker-compose.yml` — не найдёт — переменные `${VAR:?required}` упадут с сообщением «WEB_API_TOKEN required (see deploy/SECRETS.md)». Это fail-fast по дизайну.

## Ротация

### `WEB_API_TOKEN`
1. Сгенерировать новое значение (`openssl rand -hex 10` с префиксом `kb_`).
2. Поправить `/data/docker/alexwow-config/.env` на homeserver.
3. Перезапустить `alexwow-web` (`docker compose up -d alexwow-web`).
4. Синхронно обновить токен в `.mcp.json` всех dev-машин (поле `env.KANBAN_TOKEN`).

### `MYSQL_*`
1. Только при перенакате MySQL с нуля. Не ротировать на работающем стеке (поломает существующие connection-strings).
2. Если совсем нужно: остановить стек, поменять `.env`, прогнать `ALTER USER 'alexwow'@'%' IDENTIFIED BY '...'` в MySQL, поднять обратно.

## Шаблон `.env`

Реальные значения — **только** в `/data/docker/alexwow-config/.env` на homeserver. В репо
держим шаблон с плейсхолдерами; никогда не подставляем боевые токены/пароли.

```
MYSQL_ROOT_PASSWORD=<strong-random>
MYSQL_USER=<db-user>
MYSQL_PASSWORD=<strong-random>
REALM_ADDRESS=<lan-ip-of-world-server>
WEB_API_TOKEN=kb_<openssl rand -hex 10>
```

Генерация:
- `MYSQL_*` — `openssl rand -hex 16` (32 hex-символа).
- `WEB_API_TOKEN` — `openssl rand -hex 10` с префиксом `kb_` (валидация формата в `KanbanApiAuth`).

## Backup `.env`

Не бэкапим в git и не в публичные хранилища. На homeserver `/data/docker/alexwow-config/`
— `chmod 750`. Если файл потеряется — сгенерировать новые значения по разделу «Ротация»
выше и положить рядом.
