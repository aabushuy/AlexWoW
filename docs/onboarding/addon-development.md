# Разработка и тестирование аддонов

Клиентские аддоны WoW 3.3.5a (Lua 5.1) живут в [`tools/addons/`](../../tools/addons/).
Этот документ — как настроить окружение, проверять аддоны **без запуска клиента** и где вести задачи.

> Краткая справка по самим аддонам и составу проверок — [`tools/addons/README.md`](../../tools/addons/README.md).

## Трекинг задач

Все работы по аддонам ведутся в эпике **Addons** (id **3843**, project **41**) на канбан-доске
(БД `project`): <https://alexwow.home.srv/Board?project=41&epic=3843>. Новый тикет по аддону —
Task/Bug внутри этого эпика.

## Что где лежит

| Аддон | Слэш | Назначение |
|---|---|---|
| `AlexDevCmd` | `/dev` | Пульт dev-команд, телепорт, редактор характеристик, окно «Добавить вещь». |
| `AlexQATester` | `/qa` | Задачи на тестирование с канбана: список тикетов, проверка, отправка результата. |

Оба общаются с сервером addon-сообщениями (префикс `AlexDev`); каталог/данные отдаёт сервер
(`src/AlexWoW.WorldServer/Handlers/AddonProtocol.cs`).

## Установка в клиент

Клиент 3.3.5a — `C:\Wow335` или `D:\Games\WoW335` (см. локальную установку). Аддоны ставятся
копированием папки в `Interface\AddOns\`:

```powershell
# пример: симлинк, чтобы править из репо без копирования (от админа)
New-Item -ItemType SymbolicLink -Path "C:\Wow335\Interface\AddOns\AlexQATester" `
  -Target "D:\Projects\AlexWoW\tools\addons\AlexQATester"
```

После правок в игре — `/reload` (перезагрузка UI без перелогина). Ошибки Lua: включить
`/console scriptErrors 1` (или поставить BugSack/!BugGrabber).

## Проверки без клиента (главное)

Логику и корректность кода валидируем **headless** — без захода в игру. Один прогон:

```powershell
pwsh tools/addons/run-checks.ps1
```

Делает два слоя, код выхода != 0 при любом провале (пригодно для CI):

1. **luacheck** — статический анализ: синтаксис, неизвестные/опечатанные WoW-API глобалы, утечки
   в глобал, мёртвый код. Конфиг — [`tools/addons/.luacheckrc`](../../tools/addons/.luacheckrc):
   там описан std клиентского API 3.3.5a. **Используешь новый WoW-API — добавь его в `read_globals`**
   (это документирование поверхности, а не отключение проверки).
2. **luajit** — headless-тесты `tools/addons/tests/*_spec.lua`: мок клиентского API
   ([`wow_stub.lua`](../../tools/addons/tests/wow_stub.lua)) грузит аддон обычным Lua и гоняет
   чистые функции и парсеры кадров протокола через реальный событийный шов (`CHAT_MSG_ADDON`).

### Тулчейн (ставится разово, в репозиторий не коммитится)

```powershell
winget install DEVCOM.LuaJIT
# luacheck.exe — из релизов https://github.com/lunarmodules/luacheck/releases
#   положить в PATH или в %LOCALAPPDATA%\Programs\luacheck\luacheck.exe
```

`run-checks.ps1` ищет оба инструмента в `PATH` либо в
`%LOCALAPPDATA%\Programs\{LuaJIT\bin,luacheck}`.

### Как писать тест

Spec — обычный Lua-файл `tools/addons/tests/<addon>_spec.lua`:

1. `local mock = dofile(here .. "wow_stub.lua")` — установит мок WoW API в глобалы и вернёт
   `mock` (перехваты `SendAddonMessage`, `mock.fire(event, ...)` для рассылки игровых событий).
2. `dofile(here .. "../<Addon>/Core.lua")` — загрузит аддон под моком.
3. Ассерты на чистые функции неймспейса и на состояние после `mock.fire("CHAT_MSG_ADDON", prefix, line)`.
4. В конце — `os.exit(failed > 0 and 1 or 0)`.

Если аддону нужен ещё какой-то клиентский вызов — добавь заглушку в `wow_stub.lua` (там же
пояснено, какой минимум покрыт). Тестируемость растёт, когда логика вынесена в чистые функции
неймспейса (как в `AlexQATester`), а не заперта в файл-локалах.

## Визуальная проверка

Раскладку и вид окон headless не проверить — нужен живой клиент. Вид проверяется через
computer-use (скриншоты игрового окна) либо вручную. Перед визуальной проверкой убедись, что
сервер поднят и персонаж в мире (параллельный CI-пайплайн периодически рестартит тестовый сервер).
