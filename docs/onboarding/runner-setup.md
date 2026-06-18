# GitHub Actions self-hosted runners

CI/CD проекта (`.github/workflows/ci.yml`) бежит на **двух** self-hosted runner'ах:

- **homeserver-runner** — Linux, Docker-контейнер на homeserver. Labels `self-hosted,linux,docker,homeserver,deploy,dotnet`. Используется для деплоя (имеет bind-mount к `/data/docker/alexwow/`).
- **windows-runner** — Windows-сервис на dev-машине. Labels `self-hosted,windows,dotnet,devmachine`. Используется только для build+test (НЕ имеет SSH-ключей к homeserver).

Селекторы в workflow:
- `runs-on: [self-hosted, dotnet]` — любой подходящий (для `build-test`-job)
- `runs-on: [self-hosted, linux, deploy]` — только homeserver-runner (для `deploy-test`-job)

---

## Критическая безопасность ПЕРЕД установкой runner'а

**Settings → Actions → General → Fork pull request workflows from outside collaborators → "Require approval for all outside collaborators"** (или жёстче: «Require approval for first-time contributors AND all outside collaborators»).

Без этого **fork-PR может выполнить arbitrary-код на self-hosted runner'е** через `runs-on: self-hosted`. Это значит chmod 777 рут на homeserver. **Не опционально** для public-репо.

Также проверь Actions permissions: `Read and write permissions` отключить, оставить `Read repository contents and packages permissions` (минимум для checkout).

---

## homeserver-runner (Linux в Docker)

Файлы:
- `runner/docker-compose.runner.yml` — compose-файл (отдельный от основного стека, чтобы CI не пересобирал сам себя)
- `runner/.env.example` — placeholder для PAT (реальный `.env` на homeserver, не в git)

### Шаги установки

1. **Создать GitHub PAT** (fine-grained):
   - https://github.com/settings/personal-access-tokens/new
   - Owner = твой аккаунт, Resource owner = `aabushuy`
   - Repository access = только `AlexWoW`
   - Permissions:
     - Repository → Administration → **Read and write** (нужно для регистрации runner'а)
     - Repository → Actions → **Read-only**
     - Repository → Contents → **Read-only**
   - Срок действия — 90 дней (потом ротация — см. ниже).
   - Скопировать сгенерированный токен (видно один раз).

2. **Положить PAT на homeserver** в `/data/docker/alexwow-runner/.env`:
   ```bash
   sudo mkdir -p /data/docker/alexwow-runner
   sudo chown alex:alex /data/docker/alexwow-runner
   sudo chmod 750 /data/docker/alexwow-runner
   cat > /data/docker/alexwow-runner/.env <<'EOF'
   ACCESS_TOKEN=<paste your PAT here>
   REPO_URL=https://github.com/aabushuy/AlexWoW
   RUNNER_NAME=homeserver
   RUNNER_SCOPE=repo
   LABELS=self-hosted,linux,docker,homeserver,deploy,dotnet
   EOF
   chmod 640 /data/docker/alexwow-runner/.env
   ```

3. **Скопировать compose-файл и запустить:**
   ```bash
   scp runner/docker-compose.runner.yml homeserver:/data/docker/alexwow-runner/docker-compose.yml
   ssh homeserver "cd /data/docker/alexwow-runner && docker compose up -d"
   ```

4. **Проверить регистрацию:** https://github.com/aabushuy/AlexWoW/settings/actions/runners → должен появиться `homeserver` со статусом Idle (зелёный).

### Ротация PAT (каждые 90 дней)

1. Создать новый PAT (см. шаг 1 выше).
2. Заменить `ACCESS_TOKEN` в `/data/docker/alexwow-runner/.env`.
3. `ssh homeserver "cd /data/docker/alexwow-runner && docker compose up -d"` — myoung34/github-runner перерегистрируется автоматически.
4. Удалить старый PAT в GitHub Settings.

---

## windows-runner (Windows-сервис)

### Шаги установки

1. **Скачать runner archive:**
   - https://github.com/aabushuy/AlexWoW/settings/actions/runners/new
   - Выбрать Windows x64
   - GitHub покажет команды скачивания и регистрации.

2. **Создать локального юзера `gh-runner`** (НЕ Alex, НЕ admin):
   ```powershell
   # Запустить PowerShell как admin
   $password = ConvertTo-SecureString "<сгенерируй случайный>" -AsPlainText -Force
   New-LocalUser -Name "gh-runner" -Password $password -PasswordNeverExpires -UserMayNotChangePassword
   # gh-runner НЕ добавляется в Administrators
   ```

3. **Распаковать archive под `gh-runner`:**
   ```powershell
   mkdir C:\actions-runner
   cd C:\actions-runner
   # Скачать zip по ссылке из GitHub, распаковать
   icacls C:\actions-runner /grant "gh-runner:(OI)(CI)F" /T
   ```

4. **Получить registration token** (короткоживущий, 60 минут):
   - https://github.com/aabushuy/AlexWoW/settings/actions/runners/new — кнопка «Configure» внизу страницы показывает токен.
   - Token живёт час; берём непосредственно перед `config.cmd`.

5. **Зарегистрировать и поставить как сервис** (под `gh-runner`):
   ```powershell
   # Выполнять из C:\actions-runner, под Alex (admin), но --runasuser gh-runner
   .\config.cmd `
     --url https://github.com/aabushuy/AlexWoW `
     --token <RUNNER_TOKEN_FROM_GITHUB> `
     --labels self-hosted,windows,dotnet,devmachine `
     --runasservice `
     --windowslogonaccount ".\gh-runner" `
     --windowslogonpassword "<password from step 2>"
   ```

6. **Проверить:**
   - `services.msc` → должна быть служба `actions.runner.aabushuy-AlexWoW.devmachine` running.
   - https://github.com/aabushuy/AlexWoW/settings/actions/runners → `devmachine` со статусом Idle.

### Что НЕ давать windows-runner'у

- SSH-ключи к homeserver — не нужны, deploy-job только на homeserver-runner.
- Доступ к `D:\Projects\AlexWoW` — не нужен; checkout копирует свежий clone в `_work/`.
- Admin-права — не нужны; .NET 10 SDK ставится в user-scope.

### Удаление runner'а

```powershell
cd C:\actions-runner
.\config.cmd remove --token <RUNNER_TOKEN_FROM_GITHUB>
```

---

## Проверка end-to-end

После регистрации **обоих** runner'ов:

1. Сделать любой trivial-коммит в main, `git push`.
2. https://github.com/aabushuy/AlexWoW/actions — должен появиться `CI` run.
3. `build-test` job побежит на любом из двух runner'ов (по селектору `[self-hosted, dotnet]`).
4. `deploy-test` job побежит только на homeserver-runner (по селектору `[self-hosted, linux, deploy]`).
5. После завершения: `ssh homeserver "docker logs alexwow-world --tail 5"` — должен быть свежий таймстамп.

Если `build-test` зелёный, а `deploy-test` падает на «WorldServer не стартанул» — смотри логи `docker logs alexwow-world --tail 50` на homeserver (CI делает `tail 8` в smoke-проверке, но проблема может быть глубже).
