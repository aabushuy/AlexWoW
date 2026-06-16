# Kanban MCP server (KB12)

MCP-сервер канбан-доски AlexWoW: оборачивает REST API (`/api/kanban/*`, KB5) в инструменты MCP, чтобы
Claude работал с доской нативно (без curl). Без внешних зависимостей — чистый Node (stdio + JSON-RPC 2.0).

## Инструменты
- `kanban_list` — список тикетов (фильтры project/epic/status/type/tester)
- `kanban_get` — тикет + комментарии по id
- `kanban_create` — создать тикет (дерево: Epic→projectId, Task/Bug→epicId, Project — без родителей)
- `kanban_update` — обновить поля тикета
- `kanban_move` — сменить статус (колонку)
- `kanban_comment` — добавить комментарий
- `kanban_assign_tester` — авто-подбор тестировщика + client_check + перевод в Testing (KB11)

## Подключение (Claude Code)
Проектный `.mcp.json` в корне репозитория **гитигнорится** (локальный конфиг с токеном, в репозиторий не
коммитим). На этой машине он уже создан; Claude Code предложит доверять серверу при старте сессии в проекте,
после чего инструменты `kanban_*` доступны (нужен перезапуск сессии — MCP грузится на старте). На другой
машине создай корневой `.mcp.json`:

```json
{
  "mcpServers": {
    "kanban": {
      "command": "node",
      "args": ["tools/mcp/kanban-mcp.js"],
      "env": {
        "KANBAN_API_BASE": "https://alexwow.home.srv",
        "KANBAN_API_TOKEN": "<Web:ApiToken>",
        "KANBAN_TLS_INSECURE": "1"
      }
    }
  }
}
```

Конфиг через env:
- `KANBAN_API_BASE` — база Web, по умолчанию `https://alexwow.home.srv`
- `KANBAN_API_TOKEN` — токен (== `Web:ApiToken`, заголовок `X-Api-Token`)
- `KANBAN_TLS_INSECURE` — `1` (по умолчанию): не проверять самоподписанный TLS homeserver

## Проверка вручную
```sh
printf '%s\n' \
 '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}' \
 '{"jsonrpc":"2.0","id":2,"method":"tools/list"}' \
 '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"kanban_list","arguments":{"type":"Project"}}}' \
 | KANBAN_API_BASE=https://alexwow.home.srv KANBAN_API_TOKEN=<token> node tools/mcp/kanban-mcp.js
```

Источник правды у доски — БД `project` и REST API в AlexWoW.Web; MCP лишь удобный фасад над тем же API.
