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
1. `dotnet publish -c Release -o ./publish`
2. `scp publish + Dockerfile + docker-compose.yml` на сервер (в `/data/docker/alexwow`)
3. `docker compose up -d --build` (build = один тривиальный COPY-слой)

## Состав стека

| Контейнер | Образ | Порт |
|---|---|---|
| `alexwow-mysql` | `mysql:8.4` | 3306 → хост (для DBeaver/Workbench) |
| `alexwow-auth` | рантайм-образ (COPY publish/auth) | **3724/tcp** → хост |
| `alexwow-world` | рантайм-образ (COPY publish/world) | **8085/tcp** → хост |

Порты `3724` (логин) и `8085` (мир) проброшены на хост — клиент коннектится на
`192.168.2.210`. Клиентские данные (maps/vmaps/mmaps) монтируются в world-контейнер
read-only томами из каталогов **вне** деплой-каталога (`/data/docker/alexwow-{maps,vmaps,mmaps}`).

> ⚠️ Протокол WoW — **сырой TCP**, не HTTP, поэтому Caddy (reverse-proxy) не
> используется; порт пробрасывается напрямую.

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
