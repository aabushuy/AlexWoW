# Документация AlexWoW

Самописный сервер World of Warcraft на **.NET 10** + **MySQL**.
Цель — играбельный сервер под **WotLK 3.3.5a (build 12340)**. Эталон — CMaNGOS-WotLK.

## Оглавление

| Документ | О чём |
|---|---|
| [Стратегия порта CMaNGOS](strategy/cmangos-port.md) | **Source of truth.** Фазы и принципы переписывания CMaNGOS-WoTLK на C#. |
| [Карта порта](cmangos-port-map.md) | Соответствие «CMaNGOS файл → наш C#-порт» со статусами. Обновляется перед каждой задачей. |
| [Архитектура](architecture.md) | Компоненты (auth/world/web), сетевой протокол 3.3.5a, БД, эталонные исходники, риски |
| [Быстрый старт](getting-started.md) | Локальная сборка, тесты, запуск, создание аккаунта, подключение клиента |
| [Деплой](deployment.md) | Выкладка на homeserver (локальная сборка → бинарники); веб-панель за Caddy |
| [Code style](code-style.md) | C#-конвенции репо: язык, нейминг, форматирование |
| [Onboarding](onboarding/) | Setup self-hosted runners, sample-конфиги SSH/MCP |
| Дашборд `alexwow.home.srv/dashboard` | Живой прогресс по доменам (БД `project`) |

## Как ведём документацию

Прогресс ведётся **по доменам геймплея** на канбан-доске (БД `project`, веб-дашборд
`alexwow.home.srv/Board` и `/dashboard`). По одному эпику на крупную систему
(`P[N] — <домен>`).

- **`strategy/cmangos-port.md`** — стратегический разворот, sourcе of truth.
- **`architecture.md`** — справочник по дизайну; меняется реже.
- Закрыли срез домена → обновили задачу на канбане.
