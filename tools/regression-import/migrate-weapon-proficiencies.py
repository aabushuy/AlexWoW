#!/usr/bin/env python3
"""
Перенос оружейных навыков (One-Handed Axes, Bows, Polearms и т.п.) из эпика
«Общие и расовые» (661) в Воин-эпик (651). Тестер — Гномвар: воин может выучить
все типы оружия, поэтому на нём проверка proficiency-спеллов наиболее полна.

Признак: spell_template.Effect1 ∈ {25 (WEAPON), 60 (PROFICIENCY)} ИЛИ
         Effect2 = 60 (PROFICIENCY) — обычная пара (Effect1=25 + Effect2=60).

Идемпотентно: повторный запуск ничего не делает (тикеты уезжают в другой эпик).
"""
from __future__ import annotations
import argparse, json, os, re, subprocess, sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.stderr.reconfigure(encoding="utf-8", errors="replace")

SSH_HOST = os.environ.get("ALEXWOW_HOMESERVER", "homeserver")
API_BASE = "http://localhost:8090/api/kanban"
TITLE_SPELL_RE = re.compile(r"^#(\d+)\s*[·•]")

SOURCE_GENERIC_EPIC_ID = 661
TARGET_WARRIOR_EPIC_ID = 651
TESTER_NAME = "Гномвар"


def ssh_run(cmd: str, *, input_data: str | None = None, timeout: int = 60) -> str:
    proc = subprocess.run(
        ["ssh", SSH_HOST, cmd],
        input=input_data, capture_output=True, text=True,
        encoding="utf-8", timeout=timeout,
    )
    if proc.returncode != 0:
        raise RuntimeError(f"ssh failed: {proc.stderr.strip() or proc.stdout.strip()}")
    return proc.stdout


def api_get(token: str, path: str) -> object:
    return json.loads(ssh_run(f"curl -s -H 'X-Api-Token: {token}' '{API_BASE}{path}'", timeout=120))


def api_patch(token: str, path: str, body: dict) -> dict:
    payload = json.dumps(body, ensure_ascii=False)
    cmd = (
        f"curl -s -H 'X-Api-Token: {token}' -H 'Content-Type: application/json' "
        f"-X PATCH {API_BASE}{path} --data-binary @-"
    )
    out = ssh_run(cmd, input_data=payload).strip()
    return json.loads(out) if out else {}


def fetch_api_token() -> str:
    return ssh_run("grep ^WEB_API_TOKEN= /data/docker/alexwow-config/.env").strip().split("=", 1)[1]


def fetch_proficiency_spell_ids(spell_ids: list[int]) -> set[int]:
    """Из заданного списка spell_id отобрать оружейные навыки:
    Effect1 ∈ {25 (WEAPON), 60 (PROFICIENCY)} ИЛИ Effect2 = 60."""
    if not spell_ids:
        return set()
    ids_csv = ",".join(str(i) for i in spell_ids)
    sql = (
        f"SELECT Id FROM mangos.spell_template "
        f"WHERE (Effect1 IN (25, 60) OR Effect2 = 60) AND Id IN ({ids_csv});"
    )
    cmd = (
        "docker exec -i alexwow-mysql mysql -ualexwow -palexwow "
        "--default-character-set=utf8mb4 --batch --skip-column-names"
    )
    out = ssh_run(cmd, input_data=sql, timeout=60)
    result = set()
    for raw in out.splitlines():
        if not raw or raw.startswith("mysql:"): continue
        try: result.add(int(raw.strip()))
        except ValueError: continue
    return result


def fetch_tester_guid(name: str) -> int:
    sql = f"SELECT guid FROM alexwow_auth.characters WHERE name='{name}' AND is_tester=1 AND deleted_at IS NULL;"
    cmd = (
        "docker exec -i alexwow-mysql mysql -ualexwow -palexwow "
        "--default-character-set=utf8mb4 --batch --skip-column-names"
    )
    out = ssh_run(cmd, input_data=sql, timeout=30).strip()
    return int(out) if out else 0


def main() -> int:
    p = argparse.ArgumentParser(description="Перенос оружейных навыков из generic в Воин-эпик.")
    p.add_argument("--dry-run", action="store_true")
    args = p.parse_args()

    token = fetch_api_token()
    tester_guid = fetch_tester_guid(TESTER_NAME)
    if not tester_guid:
        print(f"[!] Тестер {TESTER_NAME!r} не найден", file=sys.stderr)
        return 1

    print(f"[*] Загружаю generic-тикеты (epic={SOURCE_GENERIC_EPIC_ID})...")
    generic = api_get(token, f"/tickets?epic={SOURCE_GENERIC_EPIC_ID}&archived=true")
    tasks = [t for t in generic if t.get("type") == "Task"]
    print(f"    {len(tasks)} Task-тикетов в эпике")

    spell_to_ticket: dict[int, dict] = {}
    for t in tasks:
        m = TITLE_SPELL_RE.match(t.get("title", ""))
        if m:
            spell_to_ticket[int(m.group(1))] = t

    print(f"[*] Отбираю Effect1=33 (PROFICIENCY) из {len(spell_to_ticket)} spell_id...")
    prof_spells = fetch_proficiency_spell_ids(list(spell_to_ticket.keys()))
    print(f"    proficiency-спеллов: {len(prof_spells)}")

    todo = [spell_to_ticket[sid] for sid in prof_spells]
    print(f"\n    к переносу в Воин-эпик ({TARGET_WARRIOR_EPIC_ID}) под тестером {TESTER_NAME}:")
    for t in todo:
        print(f"      #{t['id']} {t['title']}")

    moved, errors = 0, 0
    for t in todo:
        if args.dry_run:
            moved += 1
            continue
        try:
            old_labels = [l for l in (t.get("labels") or []) if l.lower() != "generic"]
            new_labels = list(dict.fromkeys(old_labels + ["regression", "warrior"]))
            patched = {
                "id": t["id"],
                "title": t["title"],
                "type": t["type"],
                "priority": t["priority"],
                "status": t["status"],
                "epicId": TARGET_WARRIOR_EPIC_ID,
                "projectId": None,
                "assignee": t.get("assignee", "Агент ИИ"),
                "testerGuid": tester_guid,
                "clientCheck": True,
                "description": t.get("description"),
                "testSteps": t.get("testSteps"),
                "expectedResult": t.get("expectedResult"),
                "labels": new_labels,
            }
            api_patch(token, f"/tickets/{t['id']}", patched)
            moved += 1
        except Exception as ex:
            errors += 1
            print(f"[!] #{t['id']}: {ex}", file=sys.stderr)

    print(f"\nИтог: перенесено {moved}, ошибок {errors}")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
