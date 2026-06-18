#!/usr/bin/env python3
# /// script
# requires-python = ">=3.11"
# dependencies = ["mysql-connector-python", "requests"]
# ///
"""
Batch-обновление test-кейсов в канбане под новый формат (см. memory feedback-test-cases):
- testSteps = тултип (синтез из spell_template) + строка [<id>][тип] EN-имя + шаги
- expectedResult = bullet-list конкретных эффектов
- labels = [<Класс>] либо [<Раса>]

Usage:
    python update-kanban-test-cases.py --dry-run            # печать в консоль, без записи
    python update-kanban-test-cases.py --dry-run --limit 5  # 5 тикетов
    python update-kanban-test-cases.py                       # боевой прогон по всем non-Done Task'ам
    python update-kanban-test-cases.py --ticket 228          # один тикет

Источники:
- mangos.spell_template — для синтеза тултипа (Effect/Aura/BasePoints/DurationIndex)
- /api/kanban/tickets — для чтения/записи (X-Api-Token)
- description тикета — для класса/расы и spell_id
"""

import argparse
import re
import sys
import time
from typing import Any

import mysql.connector
import requests
import urllib3

urllib3.disable_warnings(urllib3.exceptions.InsecureRequestWarning)
sys.stdout.reconfigure(encoding="utf-8")  # Win console падает на emoji в notes

KANBAN_BASE = "https://alexwow.home.srv/api/kanban"
KANBAN_TOKEN = "kb_REDACTED"
HEADERS = {"X-Api-Token": KANBAN_TOKEN, "Content-Type": "application/json"}
MYSQL = dict(host="192.168.2.210", user="alexwow", password="alexwow", database="mangos")

SCHOOL_NAMES = {1: "Physical", 2: "Holy", 4: "Fire", 8: "Nature", 16: "Frost", 32: "Shadow", 64: "Arcane"}
POWER_NAMES = {0: "мана", 1: "ярость", 2: "фокус", 3: "энергия", 6: "сила рун"}
STAT_NAMES = {0: "Сила", 1: "Ловкость", 2: "Выносливость", 3: "Интеллект", 4: "Дух"}

# Длительности из DurationIndex (выборка — для тех что встречаются часто).
# Полная таблица в src/AlexWoW.WorldServer/World/SpellDurations.cs.
DURATIONS_MS = {
    1: 10000, 3: 60000, 5: 300000, 6: 600000, 8: 15000, 9: 30000, 18: 20000,
    21: -1, 22: 45000, 23: 90000, 25: 180000, 27: 3000, 28: 5000, 29: 12000,
    30: 1800000, 31: 8000, 38: 11000, 106: 24000,
}


def aura_text(aura: int, bp_plus1: int, misc: int, amp_ms: int, dur_ms: int) -> str | None:
    """Человеко-читаемое описание одной APPLY_AURA по типу/value. None — не покрыто."""
    if aura == 3:  # PERIODIC_DAMAGE
        if amp_ms > 0 and dur_ms > 0:
            ticks = dur_ms // amp_ms
            return f"DoT: {bp_plus1}/тик каждые {amp_ms/1000:g}с, {ticks} тиков ({dur_ms/1000:g}с, всего {bp_plus1 * ticks})"
        return f"DoT: {bp_plus1}/тик"
    if aura == 8:  # PERIODIC_HEAL
        if amp_ms > 0 and dur_ms > 0:
            ticks = dur_ms // amp_ms
            return f"HoT: {bp_plus1}/тик каждые {amp_ms/1000:g}с, {ticks} тиков"
        return f"HoT: {bp_plus1}/тик"
    if aura == 53:  # PERIODIC_LEECH
        return f"DoT с leech: {bp_plus1}/тик (восстановление HP кастеру той же величины)"
    if aura == 22:
        return f"+{bp_plus1} к сопротивлению (маска школ {misc})"
    if aura == 29:
        idx = misc if 0 <= misc <= 4 else None
        if misc == -1:
            return f"+{bp_plus1} ко всем 5 статам (Сила/Ловкость/Выносливость/Интеллект/Дух)"
        return f"+{bp_plus1} к стату «{STAT_NAMES.get(idx, '?')}» (индекс {misc})"
    if aura == 31:
        return f"+{bp_plus1}% к скорости передвижения"
    if aura == 33:
        return f"{bp_plus1}% к скорости передвижения цели (замедление)"
    if aura == 34:
        return f"+{bp_plus1} к макс. HP"
    if aura == 36:
        return f"Форма шейпшифта (форма {misc})"
    if aura == 49:
        return f"+{bp_plus1}% к уклонению"
    if aura == 51:
        return f"+{bp_plus1}% к блоку"
    if aura == 54:
        return f"{bp_plus1}% к шансу попадания (точности)"
    if aura == 69:
        return f"Поглощение урона: пул {bp_plus1} (школы {misc})"
    if aura == 77:
        return f"Иммунитет к механике {misc}"
    if aura == 79:
        return f"+{bp_plus1}% к наносимому урону (школы {misc if misc else 'все'})"
    if aura == 87:
        return f"{bp_plus1}% к получаемому урону (школы {misc if misc else 'все'})"
    if aura == 97:
        return f"Mana Shield: поглощение урона за счёт маны (пул {bp_plus1})"
    if aura == 99:
        return f"+{bp_plus1} к силе атаки мили"
    if aura == 124:
        return f"+{bp_plus1} к силе атаки дальнего боя"
    if aura == 137:
        return f"+{bp_plus1}% ко всем статам (MOD_TOTAL_STAT_PERCENTAGE)"
    if aura == 184:
        return f"−{abs(bp_plus1)}% к шансу попадания атакующих по нам (≈ +{abs(bp_plus1)}% уклонения)"
    if aura == 282:
        return f"+{bp_plus1}% к максимальному HP"
    if aura == 158:
        return f"+{bp_plus1}% к шансу крита"
    if aura == 4:
        return "Dummy-аура (скриптовый эффект, не data-driven)"
    if aura == 23:
        return f"Периодический триггер спелла каждые {amp_ms/1000:g}с"
    if aura == 107:
        return f"{signed(bp_plus1)} к флэт-модификатору спелла (SPELLMOD_FLAT)"
    if aura == 108:
        return f"{signed(bp_plus1)}% к процентному модификатору спелла (SPELLMOD_PCT)"
    if aura == 154:
        return f"+{bp_plus1} к уровню скрытности"
    return None


def signed(n: int) -> str:
    """+5 / −10 (минус-символ U+2212 для красоты, но ASCII '-' тоже норм для CLI/JSON)."""
    return f"+{n}" if n >= 0 else f"−{-n}"


def effect_text(eff: int, bp_plus1: int, die_max: int, misc: int, amp: int, dur_ms: int, school_mask: int, trigger_id: int = 0) -> str | None:
    """Описание одного effect (без APPLY_AURA — там aura_text)."""
    if eff == 2:  # SCHOOL_DAMAGE
        school = SCHOOL_NAMES.get(school_mask, f"маска {school_mask}")
        if die_max > bp_plus1:
            return f"Direct урон: {bp_plus1}..{die_max} школы {school}"
        return f"Direct урон: {bp_plus1} школы {school}"
    if eff == 10:  # HEAL
        if die_max > bp_plus1:
            return f"Direct хил: {bp_plus1}..{die_max}"
        return f"Direct хил: {bp_plus1}"
    if eff == 30:  # ENERGIZE
        pw = POWER_NAMES.get(misc, f"тип {misc}")
        return f"+{bp_plus1} к ресурсу «{pw}»"
    if eff == 24:  # CREATE_ITEM
        return f"Создаёт предмет id={misc}, кол-во {bp_plus1}"
    if eff == 38:  # DISPEL
        return f"Диспелит ауры типа {misc}"
    if eff == 96:  # CHARGE
        return "Рывок к цели"
    if eff == 29:  # LEAP
        return "Прыжок вперёд (Blink-стиль)"
    if eff == 64:  # TRIGGER_SPELL
        return f"Триггерит спелл {trigger_id}" if trigger_id else "Триггерит другой спелл (id 0 в БД — скриптовый)"
    if eff == 80:  # ADD_COMBO_POINTS
        return f"+{bp_plus1} очков серии"
    if eff in (17, 58):  # WEAPON_DAMAGE_NOSCHOOL, WEAPON_DAMAGE — флэт-бонус к урону оружия
        return f"Урон оружия + {bp_plus1}{'..' + str(die_max) if die_max > bp_plus1 else ''}"
    if eff == 121:  # NORMALIZED_WEAPON_DMG
        return f"Нормализованный урон оружия + {bp_plus1}"
    if eff == 31:  # WEAPON_PERCENT_DAMAGE
        return f"{bp_plus1}% урона оружия"
    if eff == 68:  # INTERRUPT_CAST
        return "Прерывание каста цели + лок школы"
    if eff == 38:  # DISPEL (повтор, для верности)
        return f"Диспел: снимает ауры типа {misc}"
    return None


def synth_tooltip(t: dict) -> tuple[str, list[str]]:
    """
    Синтезирует тултип и список измеримых bullet-эффектов из spell_template-ряда.
    Возвращает (tooltip-многострочный, [bullet1, bullet2, ...]).
    """
    lines: list[str] = []
    bullets: list[str] = []
    school_mask = t.get("SchoolMask", 0)

    # Каст-стоимость/время
    cast_ms = t.get("_cast_ms", 0)
    if cast_ms > 0:
        lines.append(f"Время каста: {cast_ms/1000:g}с")
    mana_pct = t.get("ManaCostPercentage", 0)
    if mana_pct > 0:
        lines.append(f"Стоимость: {mana_pct}% базовой маны")
    elif t.get("ManaCost", 0) > 0:
        pw = POWER_NAMES.get(t.get("PowerType", 0), f"тип {t.get('PowerType')}")
        lines.append(f"Стоимость: {t['ManaCost']} {pw}")
    cd = max(t.get("RecoveryTime", 0), t.get("CategoryRecoveryTime", 0))
    if cd > 0:
        lines.append(f"Перезарядка: {cd/1000:g}с")

    # 3 эффекта
    for i in (1, 2, 3):
        eff = t.get(f"Effect{i}", 0)
        if eff == 0:
            continue
        bp = t.get(f"EffectBasePoints{i}", 0) + 1
        die = t.get(f"EffectDieSides{i}", 0)
        die_max = bp + die - 1 if die > 1 else bp
        misc = t.get(f"EffectMiscValue{i}", 0)
        amp = t.get(f"EffectAmplitude{i}", 0)
        aura = t.get(f"EffectApplyAuraName{i}", 0)
        dur_idx = t.get("DurationIndex", 0)
        dur_ms = DURATIONS_MS.get(dur_idx, 0)

        trigger = t.get(f"EffectTriggerSpell{i}", 0)
        if eff == 6 and aura:
            txt = aura_text(aura, bp, misc, amp, dur_ms)
            stub = f"APPLY_AURA type={aura}, BP={bp}, misc={misc}, amp={amp}мс (не покрыт скриптом — нужен ручной разбор)"
        else:
            txt = effect_text(eff, bp, die_max, misc, amp, dur_ms, school_mask, trigger)
            stub = f"SPELL_EFFECT type={eff}, BP={bp}, misc={misc}, trigger={trigger} (не покрыт скриптом — нужен ручной разбор)"
        if txt:
            lines.append(f"- {txt}")
            bullets.append(txt)
        else:
            lines.append(f"- {stub}")

    if dur_idx and dur_ms > 0:
        lines.append(f"Длительность: {dur_ms/1000:g}с")
    if dur_idx and DURATIONS_MS.get(dur_idx, 0) == -1:
        lines.append("Длительность: постоянно")
    return "\n".join(lines), bullets


CLASS_MAP = {
    "Воин": "warrior", "Паладин": "paladin", "Охотник": "hunter", "Разбойник": "rogue",
    "Жрец": "priest", "Рыцарь смерти": "death_knight", "Шаман": "shaman",
    "Маг": "mage", "Чернокнижник": "warlock", "Друид": "druid",
}
RACE_MAP = {
    "Человек": "human", "Орк": "orc", "Дворф": "dwarf", "Ночной эльф": "night_elf",
    "Нежить": "undead", "Таурен": "tauren", "Гном": "gnome", "Тролль": "troll",
    "Дреней": "draenei", "Эльф крови": "blood_elf",
}


def parse_meta(description: str) -> dict[str, Any]:
    """Извлекает: spell_id, kind ('абилка'/'талант'/'расовая'), label (класс или раса), spec."""
    out: dict[str, Any] = {}

    m = re.search(r"Spell ID:\s*`(\d+)`", description)
    if m:
        out["spell_id"] = int(m.group(1))

    # Первая `**...**` строка
    m = re.search(r"\*\*([^*]+)\*\*", description)
    if m:
        head = m.group(1).strip()
        # Расовая: "Ночной эльф (Альянс)"
        for rru, ren in RACE_MAP.items():
            if head.startswith(rru):
                out["kind"] = "расовая"
                out["label"] = rru
                out["race_en"] = ren
                return out
        # Класс: "Друид — Баланс (Balance)"
        for cru, cen in CLASS_MAP.items():
            if head.startswith(cru):
                out["label"] = cru
                out["class_en"] = cen
                # Спека после "—"
                if "—" in head:
                    out["spec"] = head.split("—", 1)[1].strip()
                break

    # Тип: талант vs абилка. Backup heuristic.
    if re.search(r"эпик.{0,5}148|талант", description, re.I):
        out["kind"] = "талант"
    elif "kind" not in out:
        out["kind"] = "абилка"
    return out


def extract_en_name(title: str) -> str | None:
    """`Гневное светило (Wrath)` → `Wrath`."""
    m = re.search(r"\(([^()]+)\)\s*$", title)
    return m.group(1).strip() if m else None


def build_test_steps(ticket: dict, tpl: dict, meta: dict) -> str:
    spell_id = meta["spell_id"]
    kind = meta.get("kind", "абилка")
    en_name = extract_en_name(ticket["title"]) or tpl.get("SpellName", "?")
    label = meta.get("label", "?")

    tooltip, _ = synth_tooltip(tpl)

    ru_name = ticket["title"].split("(")[0].strip()

    head = f"[{spell_id}][{kind}] {en_name}\nТултип (синтез из spell_template):\n{tooltip}\n"

    # Шаги — короткие, привязанные к классу.
    if kind == "расовая":
        steps = (
            "Шаги:\n"
            f"1. Войти персонажем расы {label}, уровень 80.\n"
            "2. Открыть окно персонажа, записать значения параметров, к которым меняет ауру (см. expectedResult).\n"
            f"3. Активировать «{ru_name}».\n"
            "4. Сравнить новые значения с прежними; сверить с expectedResult."
        )
    elif kind == "талант":
        steps = (
            "Шаги:\n"
            f"1. Войти тестировщиком класса {label} (уровень 80).\n"
            "2. Открыть окно талантов; найти данный талант в дереве.\n"
            "3. Вложить очко (или несколько ранков по описанию).\n"
            "4. Применить связанную абилку или зайти в бой; зафиксировать эффект.\n"
            "5. Сверить с expectedResult."
        )
    else:
        steps = (
            "Шаги:\n"
            f"1. Войти тестировщиком класса {label} (уровень 80).\n"
            "2. Подойти к манекену (тренировочному или лечебному, по эффекту).\n"
            f"3. Скастовать «{ru_name}» — посмотреть Combat Log и/или окно цели.\n"
            "4. Сверить значения с expectedResult."
        )
    return head + "\n" + steps


def build_expected(tpl: dict) -> str:
    _, bullets = synth_tooltip(tpl)
    if not bullets:
        return "Эффект не разобран автоматически — заполнить вручную (см. описание тикета)."
    return "\n".join(f"- {b}" for b in bullets)


def derive_labels(meta: dict, ticket: dict) -> list[str]:
    existing = list(ticket.get("labels") or [])
    label = meta.get("label")
    if label and label not in existing:
        existing.append(label)
    # Спека (Баланс/Защита/...) — отдельная подметка для классов
    spec = meta.get("spec")
    if spec and spec not in existing:
        # Удалить "(English)" хвост из спеки.
        spec_clean = re.sub(r"\s*\([^)]*\)\s*$", "", spec).strip()
        if spec_clean and spec_clean not in existing:
            existing.append(spec_clean)
    return existing


def fetch_tickets(only_ticket: int | None) -> list[dict]:
    if only_ticket is not None:
        r = requests.get(f"{KANBAN_BASE}/tickets/{only_ticket}", headers=HEADERS, verify=False, timeout=15)
        r.raise_for_status()
        return [r.json()["ticket"]]

    r = requests.get(f"{KANBAN_BASE}/tickets?project=145", headers=HEADERS, verify=False, timeout=30)
    r.raise_for_status()
    return [t for t in r.json() if t.get("type") == "Task" and t.get("status") != "Done"]


def fetch_spell(cur, spell_id: int) -> dict | None:
    cur.execute(
        """SELECT Id, SpellName, SchoolMask, ManaCost, ManaCostPercentage, PowerType,
                  RecoveryTime, CategoryRecoveryTime, CastingTimeIndex, DurationIndex,
                  Effect1, EffectApplyAuraName1, EffectBasePoints1, EffectDieSides1, EffectMiscValue1, EffectAmplitude1, EffectTriggerSpell1,
                  Effect2, EffectApplyAuraName2, EffectBasePoints2, EffectDieSides2, EffectMiscValue2, EffectAmplitude2, EffectTriggerSpell2,
                  Effect3, EffectApplyAuraName3, EffectBasePoints3, EffectDieSides3, EffectMiscValue3, EffectAmplitude3, EffectTriggerSpell3
             FROM spell_template WHERE Id=%s""",
        (spell_id,),
    )
    row = cur.fetchone()
    if row is None:
        return None
    cols = [d[0] for d in cur.description]
    return dict(zip(cols, row))


def main() -> None:
    p = argparse.ArgumentParser()
    p.add_argument("--dry-run", action="store_true")
    p.add_argument("--limit", type=int, default=0, help="0 = без лимита")
    p.add_argument("--ticket", type=int, default=None, help="один тикет по id")
    p.add_argument("--epic", type=int, default=0, help="фильтр по эпику")
    args = p.parse_args()

    print(f"Подключение к MySQL {MYSQL['host']}...", flush=True)
    db = mysql.connector.connect(**MYSQL)
    cur = db.cursor()

    print("Получение тикетов через REST...", flush=True)
    tickets = fetch_tickets(args.ticket)
    if args.epic:
        tickets = [t for t in tickets if t.get("epicId") == args.epic]
    if args.limit:
        tickets = tickets[: args.limit]
    print(f"Тикетов к обработке: {len(tickets)}", flush=True)

    ok, skipped, errors = 0, 0, 0
    for i, t in enumerate(tickets, 1):
        desc = t.get("description") or ""
        meta = parse_meta(desc)
        if "spell_id" not in meta:
            print(f"  [{i}/{len(tickets)}] #{t['id']} '{t['title'][:50]}' — НЕТ spell_id, пропуск", flush=True)
            skipped += 1
            continue

        sid = meta["spell_id"]
        tpl = fetch_spell(cur, sid)
        if tpl is None:
            print(f"  [{i}/{len(tickets)}] #{t['id']} spell_id={sid} не найден в spell_template, пропуск", flush=True)
            skipped += 1
            continue

        tpl["_cast_ms"] = 0  # CastingTimeIndex таблица SpellCastTimes у нас в коде C# — не дублируем сюда

        try:
            new_steps = build_test_steps(t, tpl, meta)
            new_expected = build_expected(tpl)
            new_labels = derive_labels(meta, t)
        except Exception as e:
            print(f"  [{i}/{len(tickets)}] #{t['id']} ОШИБКА генерации: {e}", flush=True)
            errors += 1
            continue

        if args.dry_run:
            print(f"\n=== [{i}/{len(tickets)}] #{t['id']} {t['title'][:60]} ===", flush=True)
            print("--- testSteps ---")
            print(new_steps)
            print("--- expectedResult ---")
            print(new_expected)
            print("--- labels ---", new_labels)
            ok += 1
        else:
            payload = {
                "title": t["title"],
                "epicId": t.get("epicId"),
                "testSteps": new_steps,
                "expectedResult": new_expected,
                "labels": new_labels,
            }
            try:
                r = requests.patch(
                    f"{KANBAN_BASE}/tickets/{t['id']}", json=payload, headers=HEADERS, verify=False, timeout=15
                )
                r.raise_for_status()
                ok += 1
                if i % 10 == 0:
                    print(f"  ... обработано {i}/{len(tickets)}", flush=True)
            except Exception as e:
                print(f"  [{i}/{len(tickets)}] #{t['id']} PATCH-ошибка: {e}", flush=True)
                errors += 1
            time.sleep(0.05)  # лёгкая throttle

    print(f"\nИтог: ok={ok}, skipped={skipped}, errors={errors}", flush=True)


if __name__ == "__main__":
    main()
