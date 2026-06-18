#!/usr/bin/env python3
"""
Генератор регрессионных тикетов под канбан-доску AlexWoW.

Источник: spell_template + npc_trainer на homeserver (через ssh + docker exec).
Идемпотентность: уже заведённые тикеты определяются по regex `#<spell_id> ·` в title.

Использование:
    python tools/regression-import/generate.py --dry-run --limit 5
    python tools/regression-import/generate.py --limit 5
    python tools/regression-import/generate.py
    python tools/regression-import/generate.py --family 4   # только Warrior
"""
from __future__ import annotations
import argparse, json, os, re, subprocess, sys
from pathlib import Path

# Windows cp1251 не понимает кириллицу/символы вроде ≥ — принудительный UTF-8.
sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.stderr.reconfigure(encoding="utf-8", errors="replace")

# Импорт шаблонов
sys.path.insert(0, str(Path(__file__).parent))
from template import SpellRow, build_ticket  # noqa: E402

ROOT = Path(__file__).parent
EPICS_FILE = ROOT / "epics.json"

# Параметры подключения. Всё через ssh — никаких локальных Python-зависимостей.
SSH_HOST = os.environ.get("ALEXWOW_HOMESERVER", "homeserver")
API_BASE = "http://localhost:8090/api/kanban"


def ssh_run(cmd: str, *, input_data: str | None = None, timeout: int = 60) -> str:
    """Выполнить команду на homeserver через ssh; вернуть stdout (text)."""
    proc = subprocess.run(
        ["ssh", SSH_HOST, cmd],
        input=input_data, capture_output=True, text=True,
        encoding="utf-8", timeout=timeout,
    )
    if proc.returncode != 0:
        raise RuntimeError(f"ssh {SSH_HOST!r} failed: {proc.stderr.strip() or proc.stdout.strip()}")
    return proc.stdout


def fetch_api_token() -> str:
    """Прочитать WEB_API_TOKEN из /data/docker/alexwow-config/.env на homeserver."""
    out = ssh_run("grep ^WEB_API_TOKEN= /data/docker/alexwow-config/.env").strip()
    return out.split("=", 1)[1]


# ─── SQL ───────────────────────────────────────────────────────────────────

SQL_TRAINER_SPELLS = """\
WITH src AS (
    SELECT s.Id, s.SpellName, s.SpellLevel, s.SpellFamilyName,
           s.SchoolMask, s.ManaCost, s.PowerType,
           s.Effect1, s.EffectBasePoints1, s.EffectDieSides1, s.EffectApplyAuraName1 AS EffectAura1,
           s.Effect2, s.EffectBasePoints2,
           s.Effect3, s.EffectBasePoints3,
           s.RecoveryTime, s.DurationIndex,
           0 AS IsRacial
    FROM mangos.spell_template s
    JOIN mangos.npc_trainer nt ON nt.spell = s.Id
    WHERE s.SpellName <> ''
    UNION
    -- Стартовые спеллы по race+class (расовые активки, базовые проф.навыки, языки) — учатся
    -- автоматически при создании персонажа, не через тренера. IsRacial=1 — флаг для роутинга
    -- в эпик «Расовые», если SpellFamilyName=0 (т.е. это не класс-абилка, а расовая/общая).
    SELECT s.Id, s.SpellName, s.SpellLevel, s.SpellFamilyName,
           s.SchoolMask, s.ManaCost, s.PowerType,
           s.Effect1, s.EffectBasePoints1, s.EffectDieSides1, s.EffectApplyAuraName1 AS EffectAura1,
           s.Effect2, s.EffectBasePoints2,
           s.Effect3, s.EffectBasePoints3,
           s.RecoveryTime, s.DurationIndex,
           1 AS IsRacial
    FROM mangos.spell_template s
    JOIN mangos.playercreateinfo_spell ps ON ps.Spell = s.Id
    WHERE s.SpellName <> ''
),
ranked AS (
    SELECT *,
           ROW_NUMBER() OVER (PARTITION BY SpellName, SpellFamilyName
                              ORDER BY SpellLevel DESC, Id DESC, IsRacial ASC) AS rn
    FROM src
)
SELECT Id, SpellName, SpellLevel, SpellFamilyName, SchoolMask, ManaCost, PowerType,
       Effect1, EffectBasePoints1, EffectDieSides1, EffectAura1,
       Effect2, EffectBasePoints2, Effect3, EffectBasePoints3,
       RecoveryTime, DurationIndex, IsRacial
FROM ranked WHERE rn = 1
ORDER BY SpellFamilyName, SpellLevel, Id;
"""


def fetch_spells() -> list[SpellRow]:
    """Выполнить SQL на homeserver, распарсить TSV-выход. SQL прокидывается через stdin —
    длинный CTE+UNION не помещается в командной строке как `-e "..."`."""
    cmd = (
        "docker exec -i alexwow-mysql mysql -ualexwow -palexwow "
        "--default-character-set=utf8mb4 --batch --skip-column-names"
    )
    out = ssh_run(cmd, input_data=SQL_TRAINER_SPELLS, timeout=120)
    rows: list[SpellRow] = []
    for raw_line in out.splitlines():
        if not raw_line or raw_line.startswith("mysql:"):
            continue
        parts = raw_line.split("\t")
        if len(parts) < 18:
            continue
        try:
            rows.append(SpellRow(
                Id=int(parts[0]),
                SpellName=parts[1],
                SpellLevel=int(parts[2]),
                SpellFamilyName=int(parts[3]),
                SchoolMask=int(parts[4]),
                ManaCost=int(parts[5]),
                PowerType=int(parts[6]),
                Effect1=int(parts[7]),
                EffectBasePoints1=int(parts[8]),
                EffectDieSides1=int(parts[9]),
                EffectAura1=int(parts[10]),
                Effect2=int(parts[11]),
                EffectBasePoints2=int(parts[12]),
                Effect3=int(parts[13]),
                EffectBasePoints3=int(parts[14]),
                RecoveryTime=int(parts[15]),
                DurationIndex=int(parts[16]),
                IsRacial=int(parts[17]) == 1,
            ))
        except ValueError:
            # Какая-нибудь экзотическая строка с NULL вместо числа — пропускаем.
            continue
    return rows


# ─── Идемпотентность ───────────────────────────────────────────────────────

TITLE_SPELL_RE = re.compile(r"^#(\d+)\s*[·•]")


def fetch_existing_spell_ids(token: str, project_id: int) -> set[int]:
    """Все уже созданные regression-тикеты — собираем spell_id из title по метке regression
    БЕЗ фильтра по проекту: регрессионные тикеты живут в нескольких проектах (650 «Регрессия абилок»,
    2431 «Регрессия профессий») после ручных миграций. Фильтр по project дал бы false-negative
    для перенесённых тикетов, и генератор создал бы их повторно."""
    cmd = (
        f"curl -s -H 'X-Api-Token: {token}' "
        f"'{API_BASE}/tickets?labels=regression&archived=true'"
    )
    out = ssh_run(cmd)
    try:
        data = json.loads(out)
    except json.JSONDecodeError:
        return set()
    ids: set[int] = set()
    for t in data:
        m = TITLE_SPELL_RE.match(t.get("title", ""))
        if m:
            ids.add(int(m.group(1)))
    return ids


# ─── Создание тикета ───────────────────────────────────────────────────────

def post_ticket(token: str, payload: dict) -> int:
    body = json.dumps(payload, ensure_ascii=False)
    cmd = (
        f"curl -s -H 'X-Api-Token: {token}' -H 'Content-Type: application/json' "
        f"-X POST {API_BASE}/tickets --data-binary @-"
    )
    out = ssh_run(cmd, input_data=body).strip()
    try:
        d = json.loads(out)
    except json.JSONDecodeError:
        raise RuntimeError(f"Не JSON в ответе POST: {out!r}")
    if "id" not in d:
        raise RuntimeError(f"POST вернул ошибку: {out}")
    return int(d["id"])


# ─── Main ──────────────────────────────────────────────────────────────────

def main() -> int:
    p = argparse.ArgumentParser(description="Импорт регрессионных тикетов на абилки.")
    p.add_argument("--dry-run", action="store_true", help="Не пушить; печатать JSON первых N тикетов")
    p.add_argument("--limit", type=int, default=0, help="Обрабатывать только первые N абилок")
    p.add_argument("--family", type=int, default=None, help="Фильтр по SpellFamilyName")
    args = p.parse_args()

    with EPICS_FILE.open(encoding="utf-8") as f:
        epics = json.load(f)
    project_id = epics["project_id"]
    family_to_epic_key = epics["family_to_epic"]
    epic_id_by_key = epics["epics"]

    racial_epic = epic_id_by_key.get("racial")

    def epic_for(row: "SpellRow") -> int:
        # Расовые/стартовые без класс-семейства → отдельный эпик «Расовые».
        if row.IsRacial and row.SpellFamilyName == 0 and racial_epic is not None:
            return racial_epic
        key = family_to_epic_key.get(str(row.SpellFamilyName), "generic")
        return epic_id_by_key[key]

    print(f"[*] Загружаю абилки из spell_template (через ssh {SSH_HOST})...")
    rows = fetch_spells()
    print(f"    получено {len(rows)} уникальных абилок (высший ранг каждой)")
    if args.family is not None:
        rows = [r for r in rows if r.SpellFamilyName == args.family]
        print(f"    после фильтра family={args.family}: {len(rows)}")

    if args.dry_run:
        token = "(dry-run, токен не нужен)"
        existing: set[int] = set()
    else:
        token = fetch_api_token()
        print(f"[*] Загружаю уже существующие regression-тикеты (project={project_id})...")
        existing = fetch_existing_spell_ids(token, project_id)
        print(f"    найдено {len(existing)} уже заведённых spell_id")

    todo = [r for r in rows if r.Id not in existing]
    if args.limit:
        todo = todo[: args.limit]
    print(f"[*] К обработке: {len(todo)} абилок")

    created, skipped, errors = 0, 0, 0
    for r in todo:
        try:
            payload = build_ticket(r, epic_for(r))
            # Для расовых добавим метку, чтобы было видно в фильтре.
            if r.IsRacial and r.SpellFamilyName == 0:
                labels = payload.get("labels", [])
                if "racial" not in labels:
                    payload["labels"] = list(dict.fromkeys(labels + ["racial"]))
            if args.dry_run:
                print(f"\n--- DRY id={r.Id} fam={r.SpellFamilyName} pri={payload['priority']} ---")
                print(json.dumps(payload, ensure_ascii=False, indent=2))
                created += 1
            else:
                ticket_id = post_ticket(token, payload)
                print(f"[+] #{ticket_id} ← spell {r.Id} ({r.SpellName}) pri={payload['priority']}")
                created += 1
        except Exception as ex:
            print(f"[!] spell {r.Id} ({r.SpellName}): {ex}", file=sys.stderr)
            errors += 1

    print(f"\nИтог: создано {created}, пропущено уже-существующих {len(rows) - len(todo)}, "
          f"ошибок {errors}")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
