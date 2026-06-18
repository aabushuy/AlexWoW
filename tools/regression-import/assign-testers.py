#!/usr/bin/env python3
"""
Массовое назначение тестеров на регрессионные тикеты канбана.

Логика:
* классовый спелл (SpellFamilyName ∈ FAMILY_TO_CLASS) → тестер нужного класса;
* generic / расовый (SpellFamilyName=0) → round-robin по spell_id mod N — равномерно
  распределяет ~1300 пассивов/общих между всеми тестерами, чтобы один тестер не
  получил весь generic-хвост.

Используем низкоуровневые endpoint'ы /tester + /move (а не /assign-tester) —
там фиксированный приоритет race>class>level не даёт нам сделать round-robin.

Идемпотентность: тикеты с уже непустым testerGuid пропускаются (если нужен переназнач —
сначала очисти tester_guid SQL'ом или используй --force).
"""
from __future__ import annotations
import argparse, json, os, re, subprocess, sys
from pathlib import Path

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.stderr.reconfigure(encoding="utf-8", errors="replace")

SSH_HOST = os.environ.get("ALEXWOW_HOMESERVER", "homeserver")
API_BASE = "http://localhost:8090/api/kanban"
PROJECT_ID = 650
TITLE_SPELL_RE = re.compile(r"^#(\d+)\s*[·•]")

# SpellFamilyName → wow Class.Class id (alexwow_auth.characters.class). Для семейства 0
# (Общие/расовые) класс не передаём — endpoint выберет любого подходящего по уровню.
FAMILY_TO_CLASS = {
    4: 1,   # Warrior
    10: 2,  # Paladin
    9: 3,   # Hunter
    8: 4,   # Rogue
    6: 5,   # Priest
    15: 6,  # Death Knight
    11: 7,  # Shaman
    3: 8,   # Mage
    5: 9,   # Warlock
    7: 11,  # Druid
}


def ssh_run(cmd: str, *, input_data: str | None = None, timeout: int = 60) -> str:
    proc = subprocess.run(
        ["ssh", SSH_HOST, cmd],
        input=input_data, capture_output=True, text=True,
        encoding="utf-8", timeout=timeout,
    )
    if proc.returncode != 0:
        raise RuntimeError(f"ssh {SSH_HOST!r} failed: {proc.stderr.strip() or proc.stdout.strip()}")
    return proc.stdout


def fetch_api_token() -> str:
    return ssh_run("grep ^WEB_API_TOKEN= /data/docker/alexwow-config/.env").strip().split("=", 1)[1]


def fetch_tickets(token: str) -> list[dict]:
    """Все тикеты проекта (включая Epic/Project), типов Task — фильтр в коде."""
    cmd = f"curl -s -H 'X-Api-Token: {token}' '{API_BASE}/tickets?project={PROJECT_ID}&archived=true'"
    out = ssh_run(cmd, timeout=120)
    return json.loads(out)


def fetch_spell_meta(spell_ids: list[int]) -> dict[int, tuple[int, int]]:
    """spell_id → (SpellFamilyName, SpellLevel). SQL передаётся через stdin (длинный IN не помещается в args)."""
    if not spell_ids:
        return {}
    ids_csv = ",".join(str(i) for i in spell_ids)
    sql = (
        f"SELECT Id, SpellFamilyName, SpellLevel FROM mangos.spell_template "
        f"WHERE Id IN ({ids_csv});"
    )
    # `mysql -i` (interactive) не нужен — обычный stdin-режим, читает SQL до EOF.
    cmd = (
        "docker exec -i alexwow-mysql mysql -ualexwow -palexwow "
        "--default-character-set=utf8mb4 --batch --skip-column-names"
    )
    out = ssh_run(cmd, input_data=sql, timeout=120)
    result = {}
    for raw in out.splitlines():
        if not raw or raw.startswith("mysql:"): continue
        parts = raw.split("\t")
        if len(parts) < 3: continue
        try:
            result[int(parts[0])] = (int(parts[1]), int(parts[2]))
        except ValueError:
            continue
    return result


def fetch_testers() -> list[dict]:
    """Все is_tester=1 персонажи, сортированные по class — для round-robin generic-спеллов."""
    sql = (
        "SELECT guid, name, race, class, level FROM alexwow_auth.characters "
        "WHERE is_tester=1 AND deleted_at IS NULL ORDER BY class;"
    )
    cmd = (
        "docker exec -i alexwow-mysql mysql -ualexwow -palexwow "
        "--default-character-set=utf8mb4 --batch --skip-column-names"
    )
    out = ssh_run(cmd, input_data=sql, timeout=60)
    testers = []
    for raw in out.splitlines():
        if not raw or raw.startswith("mysql:"): continue
        parts = raw.split("\t")
        if len(parts) < 5: continue
        try:
            testers.append({
                "guid": int(parts[0]), "name": parts[1],
                "race": int(parts[2]), "class": int(parts[3]), "level": int(parts[4]),
            })
        except ValueError:
            continue
    return testers


def set_tester(token: str, ticket_id: int, tester_guid: int) -> dict:
    """POST /tickets/{id}/tester — только tester_guid + client_check (без смены статуса)."""
    payload = json.dumps({"testerGuid": tester_guid, "clientCheck": True})
    cmd = (
        f"curl -s -H 'X-Api-Token: {token}' -H 'Content-Type: application/json' "
        f"-X POST {API_BASE}/tickets/{ticket_id}/tester --data-binary @-"
    )
    return json.loads(ssh_run(cmd, input_data=payload).strip())


def move_to_testing(token: str, ticket_id: int) -> dict:
    """POST /tickets/{id}/move body {status:'Testing'}."""
    payload = json.dumps({"status": "Testing"})
    cmd = (
        f"curl -s -H 'X-Api-Token: {token}' -H 'Content-Type: application/json' "
        f"-X POST {API_BASE}/tickets/{ticket_id}/move --data-binary @-"
    )
    return json.loads(ssh_run(cmd, input_data=payload).strip())


def main() -> int:
    p = argparse.ArgumentParser(description="Назначить тестеров на регрессионные тикеты.")
    p.add_argument("--dry-run", action="store_true", help="Не делать POST; печатать что бы назначилось")
    p.add_argument("--limit", type=int, default=0, help="Только первые N тикетов")
    p.add_argument("--force", action="store_true", help="Игнорировать тикеты с уже-назначенным тестером и переназначать всех")
    p.add_argument("--no-move", action="store_true", help="Только tester_guid; не переводить в Testing")
    args = p.parse_args()

    token = fetch_api_token()
    print(f"[*] Загружаю тестеров...")
    testers = fetch_testers()
    print(f"    тестеров: {len(testers)} (классы: {sorted(t['class'] for t in testers)})")
    by_class = {t["class"]: t for t in testers}

    def pick_tester(family: int, spell_id: int) -> dict:
        """Классовый — берём тестера этого класса; generic — round-robin spell_id % N."""
        cls_id = FAMILY_TO_CLASS.get(family)
        if cls_id is not None and cls_id in by_class:
            return by_class[cls_id]
        return testers[spell_id % len(testers)]

    print(f"[*] Загружаю тикеты project={PROJECT_ID}...")
    tickets = fetch_tickets(token)
    tasks = [t for t in tickets if t.get("type") == "Task"]
    print(f"    всего тикетов в проекте: {len(tickets)}; Task: {len(tasks)}")

    # spell_id → ticket
    by_spell: dict[int, dict] = {}
    skipped_no_spell, skipped_has_tester, skipped_archive = 0, 0, 0
    for t in tasks:
        if t.get("isArchive"):
            skipped_archive += 1
            continue
        if t.get("testerGuid") and not args.force:
            skipped_has_tester += 1
            continue
        m = TITLE_SPELL_RE.match(t.get("title", ""))
        if not m:
            skipped_no_spell += 1
            continue
        by_spell[int(m.group(1))] = t
    print(f"    к назначению: {len(by_spell)}  (пропущено: уже-с-тестером={skipped_has_tester}, "
          f"без-spell_id={skipped_no_spell}, архивных={skipped_archive})")

    if args.limit:
        keep = list(by_spell.keys())[:args.limit]
        by_spell = {k: by_spell[k] for k in keep}
        print(f"    после --limit: {len(by_spell)}")

    print(f"[*] Запрашиваю SpellFamilyName/SpellLevel батчем для {len(by_spell)} spell_id...")
    meta = fetch_spell_meta(list(by_spell.keys()))
    print(f"    получено мета для {len(meta)} spell'ов")

    # Подсчёт распределения по тестерам для отчёта
    dist: dict[str, int] = {}

    assigned, errors, no_meta = 0, 0, 0
    for spell_id, t in by_spell.items():
        if spell_id not in meta:
            no_meta += 1
            print(f"[!] #{t['id']} spell {spell_id}: нет в spell_template — пропускаю", file=sys.stderr)
            continue
        family, level = meta[spell_id]
        tester = pick_tester(family, spell_id)
        dist[tester["name"]] = dist.get(tester["name"], 0) + 1
        if args.dry_run:
            print(f"[dry] #{t['id']} spell {spell_id} fam={family} lvl={level} → {tester['name']} (class={tester['class']})")
            assigned += 1
            continue
        try:
            set_tester(token, t["id"], tester["guid"])
            if not args.no_move:
                move_to_testing(token, t["id"])
            assigned += 1
            if assigned % 100 == 0:
                print(f"  [{assigned}/{len(by_spell)}] last: #{t['id']} ← {tester['name']}")
        except Exception as ex:
            errors += 1
            print(f"[!] #{t['id']} spell {spell_id} fam={family} lvl={level}: {ex}", file=sys.stderr)

    print(f"\nРаспределение по тестерам:")
    for name, n in sorted(dist.items(), key=lambda kv: -kv[1]):
        print(f"  {name:14s} {n}")
    print(f"\nИтог: назначено {assigned}, без мета {no_meta}, ошибок {errors}, "
          f"уже-с-тестером пропущено {skipped_has_tester}")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
