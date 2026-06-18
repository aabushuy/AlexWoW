#!/usr/bin/env python3
"""
Перенос тикетов профессий из эпика «Общие и расовые» (661) в новый проект «Регрессия профессий».

Алгоритм:
1. Создать (если ещё нет) проект «Регрессия профессий» + 14 эпиков (по одному на профу).
2. Загрузить spell_id → [skill_line_id] из skill-line-ability.json (дамп DBC).
3. По тикетам проекта 650 (Регрессия абилок), эпика 661 (generic) — определить spell_id из title,
   найти проф-навык в нашем вайтлисте и перенести:
     * PATCH ticket → новый epic_id + labels [regression, profession] (без «generic»);
     * назначить tester_guid в соответствии с мапой профа→тестер.

Идемпотентно: повторный запуск не дублирует и не перепутывает (фильтрует по project_id).
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

SOURCE_PROJECT_ID = 650         # «Регрессия абилок»
SOURCE_GENERIC_EPIC_ID = 661    # «Общие и расовые» — где сейчас лежат профа-спеллы

# SkillLine.dbc id → (русское имя профы, slug-метка для эпика, имя тестера).
# Маппинг согласован с пользователем («по логике WoW»). Тестеры есть в alexwow_auth.characters
# по уникальным русским именам.
PROFESSIONS = {
    164: ("Кузнечное дело",    "blacksmithing",  "Гномвар"),     # Warrior
    165: ("Кожевничество",     "leatherworking", "Эльфдру"),     # Druid
    171: ("Алхимия",           "alchemy",        "Дренейшам"),   # Shaman
    182: ("Травничество",      "herbalism",      "Эльфдру"),     # Druid
    185: ("Кулинария",         "cooking",        "Таурендк"),    # DK
    186: ("Горное дело",       "mining",         "Дворфпал"),    # Paladin
    197: ("Портняжное дело",   "tailoring",      "Эльфмаг"),     # Mage
    202: ("Инженерное дело",   "engineering",    "Оркхант"),     # Hunter
    333: ("Зачарование",       "enchanting",     "Нежитьлок"),   # Warlock
    356: ("Рыбная ловля",      "fishing",        "Челприст"),    # Priest
    393: ("Снятие шкур",       "skinning",       "Трольрога"),   # Rogue
    755: ("Ювелирное дело",    "jewelcrafting",  "Дренейшам"),   # Shaman
    773: ("Начертание",        "inscription",    "Челприст"),    # Priest
    129: ("Первая помощь",     "firstaid",       "Челприст"),    # Priest
}


# ─── SSH / API helpers ─────────────────────────────────────────────────────

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
    cmd = f"curl -s -H 'X-Api-Token: {token}' '{API_BASE}{path}'"
    return json.loads(ssh_run(cmd, timeout=120))


def api_post(token: str, path: str, body: dict) -> dict:
    payload = json.dumps(body, ensure_ascii=False)
    cmd = (
        f"curl -s -H 'X-Api-Token: {token}' -H 'Content-Type: application/json' "
        f"-X POST {API_BASE}{path} --data-binary @-"
    )
    out = ssh_run(cmd, input_data=payload).strip()
    return json.loads(out) if out else {}


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


# ─── Skeleton create / locate ──────────────────────────────────────────────

def ensure_skeleton(token: str) -> tuple[int, dict[int, int]]:
    """Найти или создать Project «Регрессия профессий» + 14 эпиков. Возвращает (project_id, {skill_id: epic_id})."""
    # Ищем проект среди всех (включая архивные).
    all_tickets = api_get(token, "/tickets?archived=true")
    project = next(
        (t for t in all_tickets if t.get("type") == "Project" and t.get("title") == "Регрессия профессий"),
        None,
    )
    if project is None:
        project = api_post(token, "/tickets", {
            "title": "Регрессия профессий",
            "type": "Project",
            "priority": "Major",
            "description": "Регрессионные тикеты по спеллам профессий (Кузнечное, Алхимия, и т.д.). "
                           "Перенесены из проекта «Регрессия абилок» (эпик «Общие и расовые»). "
                           "Метка `profession` + у каждого тестировщика своя профа — удобно тестировать "
                           "всю профу одним персонажем.",
            "labels": ["regression", "profession"],
        })
        print(f"[+] Project «Регрессия профессий» #{project['id']}")
    else:
        print(f"[=] Project «Регрессия профессий» уже есть: #{project['id']}")
    project_id = int(project["id"])

    # Эпики
    by_skill: dict[int, int] = {}
    existing_epics = api_get(token, f"/tickets?project={project_id}&type=Epic&archived=true")
    by_title = {t["title"]: t["id"] for t in existing_epics}

    for skill_id, (ru_name, slug, tester_name) in PROFESSIONS.items():
        title = f"{ru_name}"
        if title in by_title:
            by_skill[skill_id] = int(by_title[title])
            continue
        epic = api_post(token, "/tickets", {
            "title": title,
            "type": "Epic",
            "priority": "Major",
            "projectId": project_id,
            "description": f"Регрессия спеллов профессии «{ru_name}» (SkillLine.dbc id={skill_id}). "
                           f"Тестировщик: {tester_name}. Метки только: regression, profession.",
            "labels": ["regression", "profession"],
        })
        by_skill[skill_id] = int(epic["id"])
        print(f"[+] Epic «{title}» #{epic['id']}")

    return project_id, by_skill


# ─── Tester lookup ─────────────────────────────────────────────────────────

def fetch_skill_via_effects(spell_ids: list[int]) -> dict[int, int]:
    """Fallback: для проф-обучателей тиров (Expert Cook и т.п.) skill-линию даёт
    Effect1/2/3 == 44 (SKILL_STEP) / 118 (SKILL), EffectMiscValueN = SkillLine id.
    Эти спеллы не входят в SkillLineAbility.dbc, поэтому без второго источника
    они уезжают в generic. Возвращает spell_id → skill_line из нашего вайтлиста."""
    if not spell_ids:
        return {}
    ids_csv = ",".join(str(i) for i in spell_ids)
    sql = (
        "SELECT Id, "
        "  CASE WHEN Effect1 IN (44,118) THEN EffectMiscValue1 "
        "       WHEN Effect2 IN (44,118) THEN EffectMiscValue2 "
        "       WHEN Effect3 IN (44,118) THEN EffectMiscValue3 "
        "       ELSE 0 END AS skill "
        f"FROM mangos.spell_template WHERE Id IN ({ids_csv});"
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
            sid, skill = int(parts[0]), int(parts[1])
            if skill in PROFESSIONS:
                result[sid] = skill
        except ValueError:
            continue
    return result


def fetch_testers_by_name() -> dict[str, int]:
    """Имя персонажа-тестера → guid (для PATCH testerGuid)."""
    sql = (
        "SELECT name, guid FROM alexwow_auth.characters "
        "WHERE is_tester=1 AND deleted_at IS NULL;"
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
        if len(parts) >= 2:
            try:
                result[parts[0]] = int(parts[1])
            except ValueError:
                continue
    return result


# ─── Main ──────────────────────────────────────────────────────────────────

def main() -> int:
    p = argparse.ArgumentParser(description="Перенос проф-спеллов из эпика 'Общие и расовые' в проект 'Регрессия профессий'.")
    p.add_argument("--dry-run", action="store_true", help="Не создавать и не PATCH'ить — показать что бы сделали")
    p.add_argument("--limit", type=int, default=0, help="Только первые N тикетов из generic-эпика")
    args = p.parse_args()

    token = fetch_api_token()

    print("[*] Загружаю карту spell_id → skill_lines из SkillLineAbility.dbc...")
    skill_map_raw: dict[str, list[int]] = json.loads((ROOT / "skill-line-ability.json").read_text(encoding="utf-8"))
    skill_map = {int(k): v for k, v in skill_map_raw.items()}
    print(f"    {len(skill_map)} spell_id с skill-привязкой")

    print("[*] Загружаю тестеров...")
    testers = fetch_testers_by_name()
    missing_testers = [n for _, _, n in PROFESSIONS.values() if n not in testers]
    if missing_testers:
        print(f"[!] Тестеры не найдены: {missing_testers}", file=sys.stderr)
        return 1

    if not args.dry_run:
        print("[*] Создаю проект и эпики (или нахожу существующие)...")
    project_id, epic_by_skill = ensure_skeleton(token) if not args.dry_run else (-1, {sk: -1 for sk in PROFESSIONS})

    print(f"[*] Загружаю generic-тикеты из source эпика {SOURCE_GENERIC_EPIC_ID}...")
    generic_tickets = api_get(token, f"/tickets?epic={SOURCE_GENERIC_EPIC_ID}&archived=true")
    print(f"    {len(generic_tickets)} тикетов в исходном эпике")

    # Собираем spell_id всех задач, чтобы потом одним SELECT подтянуть Effect-fallback.
    ticket_by_spell: dict[int, dict] = {}
    skipped_no_spell = 0
    for t in generic_tickets:
        if t.get("type") != "Task":
            continue
        m = TITLE_SPELL_RE.match(t.get("title", ""))
        if not m:
            skipped_no_spell += 1
            continue
        ticket_by_spell[int(m.group(1))] = t

    # Fallback: тир-апгрейды профы (Expert Cook и т.п.) НЕ в SkillLineAbility.dbc, но
    # их Effect == 44 (SKILL_STEP), EffectMiscValue = SkillLine id.
    spell_ids_for_fallback = [sid for sid in ticket_by_spell if sid not in skill_map]
    if spell_ids_for_fallback:
        print(f"[*] Fallback по Effect/EffectMiscValue для {len(spell_ids_for_fallback)} спеллов без DBC-привязки...")
        eff_map = fetch_skill_via_effects(spell_ids_for_fallback)
        print(f"    подобрано через эффекты: {len(eff_map)}")
    else:
        eff_map = {}

    to_migrate: list[tuple[dict, int, int]] = []   # (ticket, skill_id, target_epic_id)
    skipped_no_skill = skipped_unknown_skill = 0
    for sid, t in ticket_by_spell.items():
        # 1) Прямая запись в SkillLineAbility.dbc
        skills = skill_map.get(sid)
        chosen = next((s for s in (skills or []) if s in PROFESSIONS), None)
        # 2) Fallback по Effect/EffectMiscValue
        if chosen is None:
            chosen = eff_map.get(sid)
        if chosen is None:
            if skills:
                skipped_unknown_skill += 1
            else:
                skipped_no_skill += 1
            continue
        to_migrate.append((t, chosen, epic_by_skill[chosen]))

    print(f"    к переносу: {len(to_migrate)}  (без spell_id: {skipped_no_spell}, "
          f"без skill: {skipped_no_skill}, не-проф skill: {skipped_unknown_skill})")

    if args.limit:
        to_migrate = to_migrate[: args.limit]
        print(f"    после --limit: {len(to_migrate)}")

    # Распределение
    dist: dict[str, int] = {}
    for _, skill_id, _ in to_migrate:
        ru = PROFESSIONS[skill_id][0]
        dist[ru] = dist.get(ru, 0) + 1
    print("    распределение по профам:")
    for name, n in sorted(dist.items(), key=lambda kv: -kv[1]):
        print(f"      {name:22s} {n}")

    migrated, errors = 0, 0
    for t, skill_id, target_epic_id in to_migrate:
        ru, slug, tester_name = PROFESSIONS[skill_id]
        tester_guid = testers[tester_name]

        # PATCH тикета: меняем epicId, перезаписываем labels, ставим tester.
        # Канбан-API: PATCH /tickets/{id} принимает KanbanTicket — нужно передать ВСЕ поля.
        # Загружаем полный тикет и подменяем нужное.
        if args.dry_run:
            print(f"[dry] #{t['id']} ({t['title'][:50]!r}) → {ru} (epic={target_epic_id}, tester={tester_name})")
            migrated += 1
            continue

        try:
            # Подмена labels: убираем generic, добавляем profession (regression остаётся).
            old_labels = [l for l in (t.get("labels") or []) if l.lower() != "generic"]
            new_labels = list(dict.fromkeys(old_labels + ["profession", "regression"]))

            patched = {
                "id": t["id"],
                "title": t["title"],
                "type": t["type"],
                "priority": t["priority"],
                "status": t["status"],
                "epicId": target_epic_id,                                  # ← перенос
                "projectId": None,                                          # для Task projectId не нужен
                "assignee": t.get("assignee", "Агент ИИ"),
                "testerGuid": tester_guid,                                  # ← профа-тестер
                "clientCheck": True,
                "description": t.get("description"),
                "testSteps": t.get("testSteps"),
                "expectedResult": t.get("expectedResult"),
                "labels": new_labels,                                       # ← без generic, +profession
            }
            api_patch(token, f"/tickets/{t['id']}", patched)
            migrated += 1
            if migrated % 50 == 0:
                print(f"  [{migrated}/{len(to_migrate)}] last: #{t['id']} → {ru}")
        except Exception as ex:
            errors += 1
            print(f"[!] #{t['id']}: {ex}", file=sys.stderr)

    print(f"\nИтог: перенесено {migrated}, ошибок {errors}")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
