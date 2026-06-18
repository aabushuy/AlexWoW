# Self-hosted runners

Краткая инструкция «куда смотреть». Детальные шаги — `docs/onboarding/runner-setup.md`.

## Топология

| Runner             | Машина                       | Labels                                                      | Используется в job  |
|--------------------|------------------------------|-------------------------------------------------------------|----------------------|
| `homeserver`       | homeserver (Linux, Docker)   | `self-hosted,linux,docker,homeserver,deploy,dotnet`         | `build-test`, `deploy-test` |
| `devmachine`       | dev-Windows-машина (сервис)  | `self-hosted,windows,dotnet,devmachine`                     | `build-test` (только) |

Селектор `runs-on: [self-hosted, dotnet]` подходит обоим. Селектор `[self-hosted, linux, deploy]` — только homeserver.

## Безопасность (ОБЯЗАТЕЛЬНО до запуска runner'а)

Settings → Actions → General → Fork pull request workflows → **Require approval for all outside collaborators** (или жёстче). Иначе fork-PR с `runs-on: self-hosted` запустит произвольный код на homeserver. См. секцию «Критическая безопасность» в `docs/onboarding/runner-setup.md`.

## Файлы

- `.github/workflows/ci.yml` — workflow с job'ами `build-test` и `deploy-test`
- `runner/docker-compose.runner.yml` — compose homeserver-runner'а (на homeserver кладётся в `/data/docker/alexwow-runner/docker-compose.yml`)
- `runner/.env.example` — шаблон `.env` с PAT (реальный `.env` — `/data/docker/alexwow-runner/.env`, в `.gitignore`)
- `docs/onboarding/runner-setup.md` — пошаговая установка обоих runner'ов и ротация PAT

## Быстрый старт

См. `docs/onboarding/runner-setup.md` → секция «Шаги установки» отдельно для homeserver и windows.
