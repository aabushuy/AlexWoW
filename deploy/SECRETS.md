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
| `WEB_VIKUNJA_TOKEN`        | Bearer-токен Vikunja для дашборда (срез 2 + M12 QA-тикеты).                                         | Vikunja Settings → API tokens.           |
| `WEB_VIKUNJA_BASE_URL` / `WEB_VIKUNJA_PROJECT_ID` / `WEB_VIKUNJA_VERIFY_SSL` | Параметры подключения Vikunja. Не секреты.            | —                                        |

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

### `WEB_VIKUNJA_TOKEN`
1. Зайти на `https://tasks.home.srv` → Settings → API tokens → создать новый, скопировать.
2. Поправить `/data/docker/alexwow-config/.env`, перезапустить `alexwow-web`.
3. Старый токен отозвать в Vikunja.

### `MYSQL_*`
1. Только при перенакате MySQL с нуля. Не ротировать на работающем стеке (поломает существующие connection-strings).
2. Если совсем нужно: остановить стек, поменять `.env`, прогнать `ALTER USER 'alexwow'@'%' IDENTIFIED BY '...'` в MySQL, поднять обратно.

## Содержимое `.env` (актуальный шаблон)

```
MYSQL_ROOT_PASSWORD=rootpass
MYSQL_USER=alexwow
MYSQL_PASSWORD=alexwow
REALM_ADDRESS=192.168.2.210
WEB_API_TOKEN=kb_8f2c1a7e9d4b6035c0e1
WEB_VIKUNJA_BASE_URL=https://tasks.home.srv
WEB_VIKUNJA_TOKEN=tk_e44d354839d35c4ab12bc171b7fbd32d11428055
WEB_VIKUNJA_PROJECT_ID=11
WEB_VIKUNJA_VERIFY_SSL=false
```

Это не «секретный» документ — то же лежит на homeserver. Для ротации иди по разделам выше.

## Backup `.env`

Не бэкапим в git. На homeserver `/data/docker/alexwow-config/` — read-restricted (`chmod 750`). Если потеряется — восстановить из этого файла (значения выше) или сгенерировать новые ротацией.
