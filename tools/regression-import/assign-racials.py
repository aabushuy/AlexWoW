#!/usr/bin/env python3
"""
Распределение тестеров на расовые тикеты эпика «Расовые» (2447, проект 650).
Перевод в статус «In Progress».

Алгоритм:
1. Загрузить все Task'и из эпика 2447.
2. Для каждого тикета (spell_id из title) — найти race в mangos.playercreateinfo_spell.
   Если у спелла одна race — берём её. Если несколько (общие активки) — берём первую
   по списку (стабильный порядок).
3. По race → характер-тестер из alexwow_auth.characters (is_tester=1).
4. PATCH тикета: testerGuid + status='In Progress'.
"""
from __future__ import annotations
import argparse, json, os, re, subprocess, sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.stderr.reconfigure(encoding="utf-8", errors="replace")

SSH_HOST = os.environ.get("ALEXWOW_HOMESERVER", "homeserver")
API_BASE = "http://localhost:8090/api/kanban"
TITLE_SPELL_RE = re.compile(r"^#(\d+)\s*[·•]")

RACIAL_EPIC_ID = 2447


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


def fetch_testers_by_race() -> dict[int, tuple[int, str]]:
    """race → (guid, name) для тестеров. Один тестер на одну расу."""
    sql = (
        "SELECT race, guid, name FROM alexwow_auth.characters "
        "WHERE is_tester=1 AND deleted_at IS NULL ORDER BY race;"
    )
    cmd = (
        "docker exec -i alexwow-mysql mysql -ualexwow -palexwow "
        "--default-character-set=utf8mb4 --batch --skip-column-names"
    )
    out = ssh_run(cmd, input_data=sql, timeout=60)
    result = {}
    for raw in out.splitlines():
        if not raw or raw.startswith("mysql:"): continue
        parts = raw.split("\t")
        if len(parts) < 3: continue
        try:
            result[int(parts[0])] = (int(parts[1]), parts[2])
        except ValueError:
            continue
    return result


def fetch_spell_races(spell_ids: list[int]) -> dict[int, list[int]]:
    """spell_id → [race, ...] из playercreateinfo_spell. Стабильный порядок по race ASC."""
    if not spell_ids:
        return {}
    ids_csv = ",".join(str(i) for i in spell_ids)
    sql = (
        f"SELECT Spell, race FROM mangos.playercreateinfo_spell "
        f"WHERE Spell IN ({ids_csv}) ORDER BY Spell, race;"
    )
    cmd = (
        "docker exec -i alexwow-mysql mysql -ualexwow -palexwow "
        "--default-character-set=utf8mb4 --batch --skip-column-names"
    )
    out = ssh_run(cmd, input_data=sql, timeout=60)
    result: dict[int, list[int]] = {}
    for raw in out.splitlines():
        if not raw or raw.startswith("mysql:"): continue
        parts = raw.split("\t")
        if len(parts) < 2: continue
        try:
            sid, race = int(parts[0]), int(parts[1])
        except ValueError:
            continue
        if race in result.get(sid, []): continue
        result.setdefault(sid, []).append(race)
    return result


def main() -> int:
    p = argparse.ArgumentParser(description="Расовые тикеты → тестер по расе + In Progress.")
    p.add_argument("--dry-run", action="store_true")
    p.add_argument("--limit", type=int, default=0)
    args = p.parse_args()

    token = fetch_api_token()
    testers = fetch_testers_by_race()
    print(f"[*] Тестеров по расам: {len(testers)}")
    for race, (guid, name) in sorted(testers.items()):
        print(f"    race={race}: {name} (guid={guid})")

    print(f"\n[*] Загружаю тикеты эпика {RACIAL_EPIC_ID}...")
    tickets = api_get(token, f"/tickets?epic={RACIAL_EPIC_ID}&archived=true")
    tasks = [t for t in tickets if t.get("type") == "Task"]
    print(f"    {len(tasks)} Task-тикетов")

    spell_to_ticket: dict[int, dict] = {}
    for t in tasks:
        m = TITLE_SPELL_RE.match(t.get("title", ""))
        if m:
            spell_to_ticket[int(m.group(1))] = t
    print(f"    с распознанным spell_id: {len(spell_to_ticket)}")

    print(f"[*] Запрашиваю race для {len(spell_to_ticket)} spell_id...")
    races = fetch_spell_races(list(spell_to_ticket.keys()))
    print(f"    нашлось race-привязок: {len(races)}")

    todo: list[tuple[dict, int]] = []
    no_race, no_tester = [], []
    for sid, t in spell_to_ticket.items():
        race_list = races.get(sid, [])
        # Берём первую расу, для которой у нас есть тестер.
        race = next((r for r in race_list if r in testers), None)
        if race is None:
            if race_list:
                no_tester.append((sid, t["title"], race_list))
            else:
                no_race.append((sid, t["title"]))
            continue
        todo.append((t, race))

    if no_race:
        print(f"\n[!] {len(no_race)} тикетов без race-привязки в playercreateinfo_spell:")
        for sid, title in no_race[:5]:
            print(f"      #{sid} {title[:60]}")
    if no_tester:
        print(f"\n[!] {len(no_tester)} тикетов с расой, для которой нет тестера:")
        for sid, title, rl in no_tester[:5]:
            print(f"      #{sid} {title[:60]} race={rl}")

    if args.limit:
        todo = todo[: args.limit]

    # Распределение
    dist: dict[str, int] = {}
    for _, race in todo:
        name = testers[race][1]
        dist[name] = dist.get(name, 0) + 1
    print(f"\n[*] К назначению: {len(todo)}. Распределение:")
    for n, c in sorted(dist.items(), key=lambda kv: -kv[1]):
        print(f"      {n:14s} {c}")

    assigned, errors = 0, 0
    for t, race in todo:
        guid, name = testers[race]
        if args.dry_run:
            print(f"[dry] #{t['id']} {t['title'][:50]!r} → {name} (race={race})")
            assigned += 1
            continue
        try:
            patched = {
                "id": t["id"], "title": t["title"], "type": t["type"],
                "priority": t["priority"], "status": "In Progress",   # ← перевод
                "epicId": t["epicId"], "projectId": None,
                "assignee": t.get("assignee", "Агент ИИ"),
                "testerGuid": guid, "clientCheck": True,
                "description": t.get("description"),
                "testSteps": t.get("testSteps"),
                "expectedResult": t.get("expectedResult"),
                "labels": list(dict.fromkeys((t.get("labels") or []) + ["regression", "racial"])),
            }
            api_patch(token, f"/tickets/{t['id']}", patched)
            assigned += 1
            if assigned % 25 == 0:
                print(f"  [{assigned}/{len(todo)}] last: #{t['id']} → {name}")
        except Exception as ex:
            errors += 1
            print(f"[!] #{t['id']}: {ex}", file=sys.stderr)

    print(f"\nИтог: назначено {assigned}, ошибок {errors}")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
