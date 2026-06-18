# AlexWoW Launcher (MVP)

C++ лаунчер для клиента WoW 3.3.5a — скачивает клиента с SMB-источника, верифицирует SHA-256 по манифесту, прописывает realmlist и запускает `Wow.exe`. Без runtime-зависимостей (.NET/Java).

## Возможности MVP

- ✅ Скачивает/синхронизирует клиента из `\\192.168.2.210\WowProject\client\WoW335` в локальную папку (default `C:\Games\WoW335`).
- ✅ Сверяет SHA-256 каждого файла по `manifest.json`, докачивает только отличающиеся (паттерн rsync).
- ✅ Прописывает `WTF/Config.wtf` с `SET realmlist "192.168.2.210"`.
- ✅ Запускает `Wow.exe`.
- ❌ Логин/пароль — пользователь вводит в окне WoW (MVP, см. roadmap ниже).
- ❌ Античит / блокировка прямого запуска `wow.exe` — отдельная итерация.

## Зависимости

- **Compiler**: MSVC 19.40+ (Visual Studio 2022) или MinGW 13+ c поддержкой C++20.
- **CMake**: 3.20+.
- **WinAPI** (`bcrypt.lib`, `comctl32.lib`) — для SHA-256 и progress-bar. Уже в составе Windows SDK.
- **nlohmann/json** v3.11.3 — header-only, тянется автоматически через `FetchContent`.

## Сборка

```cmd
cd tools\WowLauncher
cmake -B build -G "Visual Studio 17 2022" -A x64
cmake --build build --config Release
```

Результат: `tools\WowLauncher\build\Release\WowLauncher.exe` (~200 КБ, статически слинкованный с CRT — `vcruntime`/`msvcp` не нужны).

## Генератор манифеста

Манифест с хешами генерируется отдельно один раз на той машине, где лежит клиент. После изменений в клиенте (новые аддоны, замена иконок, обновление DBC) — перегенерировать:

```cmd
python tools\WowLauncher\generate-manifest.py "Z:\client\WoW335"
```

Файл создаётся как `Z:\client\WoW335\manifest.json` (рядом с `Wow.exe`). Лаунчер читает его при каждом запуске.

Опции:
- `--out PATH` — куда писать (default: рядом с клиентом)
- `--version STR` — метаданные манифеста

Что НЕ попадает в манифест (волатильно):
- `WTF/` (Config, accounts, cache)
- `Cache/`, `Errors/`, `Logs/`, `Screenshots/`
- сам `manifest.json`

## Запуск

`WowLauncher.exe` → откроется окно:
1. Поле «Локальный путь WoW» (default `C:\Games\WoW335`).
2. Кнопка «Обновить и запустить» — стартует фоновую сверку.
3. Прогресс-бар + лог с детальным выводом.
4. После завершения — Wow.exe запускается, лаунчер закрывается через 2с.

## Архитектура

```
src/
├── main.cpp        — WinMain, дефолты, точка входа
├── ui.cpp/.h       — Win32 окно + диалог, фоновый поток-воркер, прогресс через PostMessage
├── manifest.cpp/.h — парсер manifest.json (nlohmann/json)
├── hasher.cpp/.h   — SHA-256 через Windows BCrypt API (без OpenSSL)
├── downloader.cpp/.h — сверка local vs source, CopyFileExW с PROGRESS_ROUTINE
├── config.cpp/.h   — патч SET realmlist в WTF/Config.wtf и Data/<locale>/realmlist.wtf
└── process.cpp/.h  — CreateProcessW(Wow.exe, cwd=root)
```

## Roadmap (вне MVP)

| Итерация | Фича | Подход |
|---|---|---|
| iter 2 | Авто-ввод логин/пароль | После запуска Wow.exe ждём окно входа через `FindWindow` + `SendInput` (эмулируем печать). Хрупко при alt-tab; пользователь может выключить в настройках. |
| iter 3 | Memory-injection sessionKey | Лаунчер сам делает SRP6 (есть готовая реализация в `src/AlexWoW.Cryptography`, портировать в C++ или подцепить через CLR-host). WriteProcessMemory в Wow.exe в известный адрес sessionKey-буфера. Требует RE WoW.exe и стабильных адресов. |
| iter 4 | Античит-токен | Лаунчер генерирует одноразовый токен → пишет в окружение/реестр перед запуском Wow.exe. Сервер проверяет токен в CMSG_AUTH_SESSION (расширение опкода или WardenChallenge). Без лаунчера токена нет → отказ в авторизации на world-сервере. |
| iter 5 | Самообновление | Простая проверка версии бинарника (HTTP-эндпоинт на alexwow.home.srv) + перезапись через ShellExecute. |

## Troubleshooting

- **«manifest.json не найден»** — запусти генератор на источнике.
- **«CreateFileW failed (5)»** — нет прав чтения SMB-шары. Проверь `net use Z: \\192.168.2.210\WowProject /persistent:yes` и креденшалы.
- **Прогресс висит на одном файле** — нормально для `Data/common.MPQ` (~1.7 ГБ). Лог напишет следующий файл когда этот закончится.
- **«CreateProcessW failed (193)»** — пытаешься запустить не 64-bit лаунчер на 32-bit Wow.exe; не критично, исходный WoW 3.3.5a — 32-bit, а лаунчер 64-bit; межбитовый запуск работает.
