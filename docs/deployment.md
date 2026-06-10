# Деплой на домашний сервер

Сервер: **homeserver** (`192.168.2.210`, Ubuntu, Docker + Compose).

## Принцип: сборка локальная, на сервер едут бинарники

Компиляция выполняется **локально** (`dotnet publish`). На сервер копируются только
готовые бинарники (`./publish`) + `Dockerfile` + `docker-compose.yml`. На сервере
**нет SDK, restore и компиляции** — рантайм-образ просто копирует `./publish`.

> Почему так: серверные docker-сборки (SDK-образ, restore по сети, компиляция на
> слабом Xeon) уже были источником проблем. Локальная сборка быстрее и надёжнее.

## Одна команда

```powershell
# с Windows-машины, из корня репозитория
./deploy/deploy.ps1
```

Скрипт [deploy/deploy.ps1](../deploy/deploy.ps1):
1. `dotnet publish -c Release -o ./publish` (auth + world + **web**)
2. `scp publish + Dockerfile.{auth,world,web} + docker-compose.yml` на сервер (в `/data/docker/alexwow`)
3. `docker compose up -d --build` (build = один тривиальный COPY-слой)

## Состав стека

| Контейнер | Образ | Порт |
|---|---|---|
| `alexwow-mysql` | `mysql:8.4` | 3306 → хост (для DBeaver/Workbench) |
| `alexwow-auth` | рантайм-образ (COPY publish/auth) | **3724/tcp** → хост |
| `alexwow-world` | рантайм-образ (COPY publish/world) | **8085/tcp** → хост |
| `alexwow-web` | `aspnet:10.0` (COPY publish/web) | **8090 → 8080** (HTTP; наружу через Caddy) |

Порты `3724` (логин) и `8085` (мир) проброшены на хост — клиент коннектится на
`192.168.2.210`. Клиентские данные (maps/vmaps/mmaps) монтируются в world-контейнер
read-only томами из каталогов **вне** деплой-каталога (`/data/docker/alexwow-{maps,vmaps,mmaps}`).

> ⚠️ Протокол WoW (логин/мир) — **сырой TCP**, не HTTP, поэтому Caddy (reverse-proxy) для него
> не используется; порт пробрасывается напрямую. Через Caddy идёт только веб-панель (HTTP).

## Веб-панель за Caddy (M8)

Веб-панель игрока (`alexwow-web`) — единственный HTTP-сервис стека, отдаётся наружу через **Caddy**
по домену **`https://alexwow.home.srv`** (сертификат wildcard `*.home.srv`).

- Caddy живёт в **отдельном стеке** homeserver (`/data/docker/caddy/Caddyfile`, **не в этом репо**),
  как и для Vikunja/прочих сервисов. Стек alexwow в своей docker-сети, поэтому Caddy проксирует
  **по host-IP** (как plex/sonarr), а не по имени контейнера:
  ```caddy
  https://alexwow.home.srv {
    tls /etc/caddy/certs/home.srv.crt /etc/caddy/certs/home.srv.key
    reverse_proxy 192.168.2.210:8090 {
      header_up X-Forwarded-Proto https
    }
  }
  ```
- Правку Caddyfile применяют **вручную на сервере** + reload:
  `docker exec caddy caddy reload --config /etc/caddy/Caddyfile`.
- Ключи Data Protection (cookie/antiforgery) — в named-volume `alexwow-web-keys` (переживают
  пересборку контейнера; иначе каждый деплой разлогинивал бы всех).
- Логин на сайт — по **email**; в игру — по отдельному **имени аккаунта** (см.
  [devlog/M8-web-interface.md](devlog/M8-web-interface.md)).

## Создать аккаунт на сервере

```bash
docker compose exec alexwow-auth dotnet AlexWoW.AuthServer.dll create-account test test
```

## Параметры

Через переменные окружения (`docker-compose.yml`):

| Переменная | Назначение | По умолчанию |
|---|---|---|
| `AuthServer__ConnectionString` | подключение к MySQL | контейнер `alexwow-mysql` |
| `AuthServer__DefaultRealm__Address` | IP world-сервера для клиента | `192.168.2.210` |
| `MYSQL_USER` / `MYSQL_PASSWORD` | креды БД | `alexwow` / `alexwow` |

> Данные MySQL хранятся в named-volume `alexwow_alexwow-mysql-data` (переживают
> пересборку и очистку каталога деплоя).
