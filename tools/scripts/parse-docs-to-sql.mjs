// Перенос трекинга из markdown (docs/classes, docs/races) в SQL-сид для БД `project`.
// Запуск:  node tools/scripts/parse-docs-to-sql.mjs > deploy/sql/project-seed.sql
// Парсит markdown-таблицы под ##/###-заголовками. БД — источник истины (после миграции md → archive).
import { readFileSync, readdirSync } from 'node:fs';
import { join } from 'node:path';

const ROOT = process.cwd();
const CLASSES = join(ROOT, 'docs', 'classes');
const RACES = join(ROOT, 'docs', 'races');

const sq = (s) => `'${String(s ?? '').replace(/'/g, "''")}'`;
const statusEmoji = (cell) => { const m = String(cell).match(/[✅🟡⬜➖]/u); return m ? m[0] : ''; };
const isSeparator = (cells) => cells.every(c => /^:?-{2,}:?$/.test(c.trim()));
const HEADER_FIRST = new Set(['Абилка', 'Талант', 'Что', 'Аура', 'Ресурс', 'Класс', 'Тип', 'Спелл']);
const isHeader = (cells) => HEADER_FIRST.has(cells[0]?.trim()) || cells.some(c => c.trim() === 'Статус');

// Разбить строку таблицы `| a | b | c |` → ['a','b','c'].
const splitRow = (line) => {
  let s = line.trim();
  if (s.startsWith('|')) s = s.slice(1);
  if (s.endsWith('|')) s = s.slice(0, -1);
  return s.split('|').map(c => c.trim());
};

// Пройти по таблицам файла, вызывая cb(cells, { h2, h3 }) на каждой строке данных.
function walkTables(text, cb) {
  let h2 = '', h3 = '';
  for (const raw of text.split(/\r?\n/)) {
    const line = raw.trimEnd();
    if (line.startsWith('### ')) { h3 = line.slice(4).trim(); continue; }
    if (line.startsWith('## ')) { h2 = line.slice(3).trim(); h3 = ''; continue; }
    if (!line.trim().startsWith('|')) continue;
    const cells = splitRow(line);
    if (isSeparator(cells) || isHeader(cells)) continue;
    cb(cells, { h2, h3 });
  }
}

const h1Name = (text) => {
  const m = text.match(/^#\s+(.+)$/m);
  return m ? m[1].split(/[—(]/)[0].trim() : '';
};

const rows = { Mechanics: [], ClassAbilities: [], ClassTalents: [], RacesAbilities: [] };

// --- classes/*-abilities.md и *-talents.md + mechanics.md ---
for (const file of readdirSync(CLASSES)) {
  if (!file.endsWith('.md') || file === 'README.md') continue;
  const text = readFileSync(join(CLASSES, file), 'utf8');

  if (file === 'mechanics.md') {
    walkTables(text, (c, { h2, h3 }) => {
      rows.Mechanics.push([h2, h3, c[0] || '', c[1] || '', statusEmoji(c[2] || ''), c[2] || '']);
    });
    continue;
  }
  const cls = h1Name(text);
  if (file.endsWith('-abilities.md')) {
    walkTables(text, (c, { h2 }) => {
      rows.ClassAbilities.push([cls, h2, c[0] || '', c[1] || '', c[2] || '', c[3] || '', statusEmoji(c[4] || ''), c[4] || '']);
    });
  } else if (file.endsWith('-talents.md')) {
    walkTables(text, (c, { h2 }) => {
      rows.ClassTalents.push([cls, h2, c[0] || '', c[1] || '', c[2] || '', c[3] || '', statusEmoji(c[4] || ''), c[4] || '']);
    });
  }
}

// --- races/*.md ---
for (const file of readdirSync(RACES)) {
  if (!file.endsWith('.md') || file === 'README.md') continue;
  const text = readFileSync(join(RACES, file), 'utf8');
  const race = h1Name(text);
  const fm = text.match(/Фракция:\s*([^\s.]+)/);
  const faction = fm ? fm[1] : '';
  walkTables(text, (c) => {
    rows.RacesAbilities.push([race, faction, c[0] || '', c[1] || '', c[2] || '', c[3] || '', statusEmoji(c[4] || ''), c[4] || '']);
  });
}

// --- эмит SQL ---
const out = [];
out.push('-- Сгенерировано tools/scripts/parse-docs-to-sql.mjs из docs/classes + docs/races. Не править вручную.');
out.push('SET NAMES utf8mb4;');
out.push('USE project;');
const dump = (table, cols, data) => {
  out.push(`TRUNCATE TABLE project.${table};`);
  for (const r of data)
    out.push(`INSERT INTO project.${table} (${cols.join(', ')}) VALUES (${r.map(sq).join(', ')});`);
};
dump('Mechanics', ['phase', 'section', 'item', 'classes', 'status', 'note'], rows.Mechanics);
dump('ClassAbilities', ['class', 'tab', 'ability', 'spell_id', 'school_aura', 'type', 'status', 'note'], rows.ClassAbilities);
dump('ClassTalents', ['class', 'tree', 'talent', 'spell_id', 'effect', 'type', 'status', 'note'], rows.ClassTalents);
dump('RacesAbilities', ['race', 'faction', 'ability', 'spell_id', 'school_aura_effect', 'type', 'status', 'note'], rows.RacesAbilities);

process.stderr.write(`rows: Mechanics=${rows.Mechanics.length} ClassAbilities=${rows.ClassAbilities.length} ClassTalents=${rows.ClassTalents.length} RacesAbilities=${rows.RacesAbilities.length}\n`);
process.stdout.write(out.join('\n') + '\n');
