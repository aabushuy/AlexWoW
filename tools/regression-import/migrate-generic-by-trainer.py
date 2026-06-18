#!/usr/bin/env python3
"""
Перенос classовых спеллов из эпика «Общие и расовые» (661) в правильные классовые эпики
по `creature_template.TrainerClass` через npc_trainer.

Когда работает: спелл попал в generic, потому что у него SpellFamilyName=0 (новые WotLK-абилки,
расовые активки, нестандартные ауры). Но если его учит классовый тренер (например «Наставник
рыцарей смерти», TrainerClass=6) — реальный класс известен и тикет можно перенести.

Идемпотентно: повторный запуск не дублирует. Метки: убираем `generic`, добавляем класс-метку
(`deathknight`/`warlock`/...), `regression` остаётся.
"""
from __future__ import annotations
import argparse, json, os, re, subprocess, sys
from pathlib import Path

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.stderr.reconfigure(encoding="utf-8", errors="replace")

ROOT = Path(__file__).parent
SSH_HOST = os.environ.get("ALEXWOW_HOMESERVER", "homeserver")
API_BASE = "http://localhost:8090/api/kanban"
TITLE_SPELL_RE = re.compile(r"^#(\d+)\s*[·•]")

SOURCE_GENERIC_EPIC_ID = 661

# wow Class.Class id → (epic_id из epics.json, метка-slug, имя-тестера-в-БД).
# Имена тестеров — см. alexwow_auth.characters (is_tester=1).
CLASS_MAP = {
    1:  (651, "warrior",     "Гномвар"),
    2:  (652, "paladin",     "Дворфпал"),
    3:  (653, "hunter",      "Оркхант"),
    4:  (654, "rogue",       "Трольрога"),
    5:  (655, "priest",      "Челприст"),
    6:  (660, "deathknight", "Таурендк"),
    7:  (656, "shaman",      "Дренейшам"),
    8:  (657, "mage",        "Эльфмаг"),
    9:  (658, "warlock",     "Нежитьлок"),
    11: (659, "druid",       "Эльфдру"),
}


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


def fetch_trainer_classes(spell_ids: list[int]) -> dict[int, int]:
    """spell_id → TrainerClass (если есть классовый тренер). SQL через stdin."""
    if not spell_ids:
        return {}
    ids_csv = ",".join(str(i) for i in spell_ids)
    sql = (
        "SELECT nt.spell, MAX(ct.TrainerClass) AS cls "
        "FROM mangos.npc_trainer nt "
        "JOIN mangos.creature_template ct ON ct.entry = nt.entry "
        f"WHERE nt.spell IN ({ids_csv}) AND ct.TrainerClass <> 0 "
        "GROUP BY nt.spell;"
    )
    cmd = (
        "docker exec -i alexwow-mysql mysql -ualexwow -palexwow "
        "--default-character-set=utf8mb4 --batch --skip-column-names"
    )
    out = ssh_run(cmd, input_data=sql, timeout=120)
    result = {}
    for raw in out.splitlines():
        if not raw or raw.startswith("mysql:"): continue
        parts = raw.split("\t")
        if len(parts) < 2: continue
        try:
            sid, cls = int(parts[0]), int(parts[1])
            if cls in CLASS_MAP:
                result[sid] = cls
        except ValueError:
            continue
    return result


def fetch_testers_by_name() -> dict[str, int]:
    sql = "SELECT name, guid FROM alexwow_auth.characters WHERE is_tester=1 AND deleted_at IS NULL;"
    cmd = (
        "docker exec -i alexwow-mysql mysql -ualexwow -palexwow "
        "--default-character-set=utf8mb4 --batch --skip-column-names"
    )
    out = ssh_run(cmd, input_data=sql, timeout=60)
    result = {}
    for raw in out.splitlines():
        if not raw or raw.startswith("mysql:"): continue
        parts = raw.split("\t")
        if len(parts) >= 2:
            try: result[parts[0]] = int(parts[1])
            except ValueError: continue
    return result


def main() -> int:
    p = argparse.ArgumentParser(description="Перенос classовых спеллов из generic по trainer_class.")
    p.add_argument("--dry-run", action="store_true")
    p.add_argument("--limit", type=int, default=0)
    args = p.parse_args()

    token = fetch_api_token()
    testers = fetch_testers_by_name()
    missing = [n for _, (_, _, n) in CLASS_MAP.items() if n not in testers]
    if missing:
        print(f"[!] Тестеры не найдены: {missing}", file=sys.stderr)
        return 1

    print(f"[*] Загружаю generic-тикеты (epic={SOURCE_GENERIC_EPIC_ID})...")
    generic = api_get(token, f"/tickets?epic={SOURCE_GENERIC_EPIC_ID}&archived=true")
    tasks = [t for t in generic if t.get("type") == "Task"]
    print(f"    {len(tasks)} Task-тикетов в эпике generic")

    spell_to_ticket: dict[int, dict] = {}
    for t in tasks:
        m = TITLE_SPELL_RE.match(t.get("title", ""))
        if m:
            spell_to_ticket[int(m.group(1))] = t

    print(f"[*] Запрашиваю TrainerClass для {len(spell_to_ticket)} spell_id...")
    spell_class = fetch_trainer_classes(list(spell_to_ticket.keys()))
    print(f"    спеллов с классовым тренером: {len(spell_class)}")

    todo = [(spell_to_ticket[sid], cls) for sid, cls in spell_class.items()]
    if args.limit:
        todo = todo[: args.limit]

    # Сводка по классам
    dist: dict[str, int] = {}
    for _, cls in todo:
        slug = CLASS_MAP[cls][1]
        dist[slug] = dist.get(slug, 0) + 1
    print("    распределение по классам:")
    for s, n in sorted(dist.items(), key=lambda kv: -kv[1]):
        print(f"      {s:14s} {n}")

    moved, errors = 0, 0
    for t, cls in todo:
        epic_id, slug, tester_name = CLASS_MAP[cls]
        tester_guid = testers[tester_name]
        if args.dry_run:
            print(f"[dry] #{t['id']} {t['title'][:60]!r} → epic={epic_id} ({slug}), tester={tester_name}")
            moved += 1
            continue
        try:
            old_labels = [l for l in (t.get("labels") or []) if l.lower() != "generic"]
            new_labels = list(dict.fromkeys(old_labels + ["regression", slug]))
            patched = {
                "id": t["id"],
                "title": t["title"],
                "type": t["type"],
                "priority": t["priority"],
                "status": t["status"],
                "epicId": epic_id,
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
            if moved % 25 == 0:
                print(f"  [{moved}/{len(todo)}] last: #{t['id']} → {slug}")
        except Exception as ex:
            errors += 1
            print(f"[!] #{t['id']}: {ex}", file=sys.stderr)

    print(f"\nИтог: перенесено {moved}, ошибок {errors}")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
