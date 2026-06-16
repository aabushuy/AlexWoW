#!/usr/bin/env node
/*
 * KB12 — MCP-сервер канбан-доски AlexWoW. Оборачивает REST API (/api/kanban/*, KB5) в MCP-инструменты,
 * чтобы Claude работал с доской нативно, без curl. Без внешних зависимостей: stdio + JSON-RPC 2.0
 * (сообщения построчно, по одному JSON на строку — транспорт MCP stdio).
 *
 * Конфигурация через env:
 *   KANBAN_API_BASE  — база, напр. https://alexwow.home.srv
 *   KANBAN_API_TOKEN — токен (заголовок X-Api-Token), как Web:ApiToken
 *   KANBAN_TLS_INSECURE — "1" (по умолчанию) не проверять самоподписанный TLS homeserver.
 */
'use strict';
const http = require('http');
const https = require('https');
const { URL } = require('url');

const BASE = (process.env.KANBAN_API_BASE || 'https://alexwow.home.srv').replace(/\/+$/, '');
const TOKEN = process.env.KANBAN_API_TOKEN || '';
const INSECURE = (process.env.KANBAN_TLS_INSECURE || '1') === '1';
const SERVER_INFO = { name: 'alexwow-kanban', version: '1.0.0' };

// ---- HTTP к REST API ----
function api(method, path, body) {
  return new Promise((resolve, reject) => {
    const u = new URL(BASE + path);
    const mod = u.protocol === 'http:' ? http : https;
    const data = body != null ? Buffer.from(JSON.stringify(body), 'utf8') : null;
    const opts = {
      method, hostname: u.hostname, port: u.port || (u.protocol === 'http:' ? 80 : 443),
      path: u.pathname + u.search, rejectUnauthorized: !INSECURE,
      headers: { 'X-Api-Token': TOKEN, 'Accept': 'application/json' },
    };
    if (data) { opts.headers['Content-Type'] = 'application/json'; opts.headers['Content-Length'] = data.length; }
    const req = mod.request(opts, (res) => {
      const chunks = [];
      res.on('data', (c) => chunks.push(c));
      res.on('end', () => {
        const text = Buffer.concat(chunks).toString('utf8');
        resolve({ status: res.statusCode, text });
      });
    });
    req.on('error', reject);
    if (data) req.write(data);
    req.end();
  });
}

// Поля тикета для create/update (общая схема).
const TICKET_FIELDS = {
  title: { type: 'string' }, type: { type: 'string', enum: ['Task', 'Bug', 'Epic', 'Project'] },
  priority: { type: 'string', enum: ['Blocker', 'Major', 'Minor'] },
  status: { type: 'string', enum: ['Backlog', 'Ready to Implementation', 'In Progress', 'Testing', 'Done'] },
  projectId: { type: ['integer', 'null'] }, epicId: { type: ['integer', 'null'] },
  assignee: { type: 'string' }, testerGuid: { type: ['integer', 'null'] }, clientCheck: { type: 'boolean' },
  description: { type: ['string', 'null'] }, testSteps: { type: ['string', 'null'] }, expectedResult: { type: ['string', 'null'] },
};

const TOOLS = [
  { name: 'kanban_list', description: 'Список тикетов доски с фильтрами (project/epic/status/type/tester).',
    inputSchema: { type: 'object', properties: { project: { type: 'integer' }, epic: { type: 'integer' },
      status: { type: 'string' }, type: { type: 'string' }, tester: { type: 'integer' } } } },
  { name: 'kanban_get', description: 'Тикет по id + комментарии.',
    inputSchema: { type: 'object', properties: { id: { type: 'integer' } }, required: ['id'] } },
  { name: 'kanban_create', description: 'Создать тикет. Дерево: Epic требует projectId; Task/Bug требуют epicId; Project — без родителей.',
    inputSchema: { type: 'object', properties: TICKET_FIELDS, required: ['title', 'type'] } },
  { name: 'kanban_update', description: 'Обновить поля тикета (id обязателен).',
    inputSchema: { type: 'object', properties: Object.assign({ id: { type: 'integer' } }, TICKET_FIELDS), required: ['id'] } },
  { name: 'kanban_move', description: 'Сменить статус тикета (колонку).',
    inputSchema: { type: 'object', properties: { id: { type: 'integer' }, status: { type: 'string' } }, required: ['id', 'status'] } },
  { name: 'kanban_comment', description: 'Добавить комментарий к тикету.',
    inputSchema: { type: 'object', properties: { id: { type: 'integer' }, body: { type: 'string' }, author: { type: 'string' } }, required: ['id', 'body'] } },
  { name: 'kanban_assign_tester', description: 'Авто-подбор персонажа-тестировщика под задачу: ставит tester + client_check и переводит в Testing. Подсказки class/level опциональны.',
    inputSchema: { type: 'object', properties: { id: { type: 'integer' }, class: { type: 'integer' }, level: { type: 'integer' }, clientCheck: { type: 'boolean' } }, required: ['id'] } },
];

function pickTicketBody(a) {
  const b = {};
  for (const k of Object.keys(TICKET_FIELDS)) if (a[k] !== undefined) b[k] = a[k];
  return b;
}

async function callTool(name, a) {
  a = a || {};
  switch (name) {
    case 'kanban_list': {
      const q = new URLSearchParams();
      for (const k of ['project', 'epic', 'status', 'type', 'tester']) if (a[k] !== undefined) q.set(k, a[k]);
      return api('GET', '/api/kanban/tickets' + (q.toString() ? '?' + q : ''));
    }
    case 'kanban_get': return api('GET', `/api/kanban/tickets/${a.id}`);
    case 'kanban_create': return api('POST', '/api/kanban/tickets', pickTicketBody(a));
    case 'kanban_update': return api('PATCH', `/api/kanban/tickets/${a.id}`, pickTicketBody(a));
    case 'kanban_move': return api('POST', `/api/kanban/tickets/${a.id}/move`, { status: a.status });
    case 'kanban_comment': return api('POST', `/api/kanban/tickets/${a.id}/comments`, { author: a.author, body: a.body });
    case 'kanban_assign_tester': return api('POST', `/api/kanban/tickets/${a.id}/assign-tester`,
      { class: a.class, level: a.level, clientCheck: a.clientCheck });
    default: throw new Error('Неизвестный инструмент: ' + name);
  }
}

// ---- JSON-RPC / MCP ----
function send(msg) { process.stdout.write(JSON.stringify(msg) + '\n'); }
function reply(id, result) { send({ jsonrpc: '2.0', id, result }); }
function replyErr(id, code, message) { send({ jsonrpc: '2.0', id, error: { code, message } }); }

let pending = 0, stdinEnded = false;
function maybeExit() { if (stdinEnded && pending === 0) process.exit(0); }

async function handle(msg) {
  const { id, method, params } = msg;
  const isNotification = id === undefined || id === null;
  try {
    if (method === 'initialize') {
      reply(id, { protocolVersion: '2024-11-05', capabilities: { tools: {} }, serverInfo: SERVER_INFO });
    } else if (method === 'tools/list') {
      reply(id, { tools: TOOLS });
    } else if (method === 'tools/call') {
      const res = await callTool(params && params.name, params && params.arguments);
      const isErr = res.status >= 400;
      reply(id, {
        content: [{ type: 'text', text: `HTTP ${res.status}\n${res.text}` }],
        isError: isErr || undefined,
      });
    } else if (method === 'ping') {
      reply(id, {});
    } else if (isNotification) {
      // notifications/initialized и пр. — без ответа.
    } else {
      replyErr(id, -32601, 'Method not found: ' + method);
    }
  } catch (e) {
    if (!isNotification) replyErr(id, -32603, String(e && e.message || e));
  }
}

let buf = '';
process.stdin.setEncoding('utf8');
process.stdin.on('data', (chunk) => {
  buf += chunk;
  let nl;
  while ((nl = buf.indexOf('\n')) >= 0) {
    const line = buf.slice(0, nl).trim();
    buf = buf.slice(nl + 1);
    if (!line) continue;
    let msg; try { msg = JSON.parse(line); } catch { continue; }
    pending++;
    handle(msg).finally(() => { pending--; maybeExit(); });
  }
});
process.stdin.on('end', () => { stdinEnded = true; maybeExit(); });
process.stderr.write(`[kanban-mcp] base=${BASE} token=${TOKEN ? 'set' : 'MISSING'}\n`);
