"""
Шаблоны для регрессионных тикетов на абилки. Из чистой Python-стандартной библиотеки.

Главный entry-point — build_ticket(row, epic_id) → dict (JSON-payload для POST /api/kanban/tickets).
Маппинги (школы, эффекты, ауры, классы) — минимальные, только для нашего scope (классовые активки).
Незнакомые коды отдаются как «#{number}» — это лучше, чем пропускать или падать.
"""
from __future__ import annotations
from dataclasses import dataclass


# ─── Маппинги ───────────────────────────────────────────────────────────────

SCHOOL_MASK = {
    1: "Физическая", 2: "Священная", 4: "Огонь", 8: "Природа",
    16: "Лёд", 32: "Тень", 64: "Тайная магия",
}

POWER_TYPE = {
    0: "мана", 1: "ярость", 2: "фокус", 3: "энергия",
    5: "здоровье", 6: "руническая сила", 7: "руны",
}

FAMILY_NAME = {
    0: "Общие/расовые", 3: "Маг", 4: "Воин", 5: "Чернокнижник",
    6: "Жрец", 7: "Друид", 8: "Разбойник", 9: "Охотник",
    10: "Паладин", 11: "Шаман", 15: "Рыцарь смерти",
}

# Самые распространённые SpellEffect (см. CMaNGOS SharedDefines.h). Незнакомые → "#N".
EFFECT_NAME = {
    0: "—",
    2: "SCHOOL_DAMAGE (прямой урон школой)",
    3: "DUMMY",
    6: "APPLY_AURA",
    8: "ENERGIZE (восстановление ресурса)",
    10: "HEAL (прямое лечение)",
    24: "CREATE_ITEM",
    26: "OPEN_LOCK",
    30: "ENERGIZE_PCT",
    35: "POWER_BURN",
    38: "INTERRUPT_CAST",
    62: "CAST_BUTTON",
    64: "TRIGGER_SPELL",
    77: "SCRIPT_EFFECT",
    80: "POWER_DRAIN_PCT",
    108: "APPLY_GLYPH",
    113: "PROFICIENCY",
    127: "PROSPECTING",
    137: "MILLING",
    142: "JUMP",
    150: "REDIRECT_THREAT",
}

# SpellAura (самые частые)
AURA_NAME = {
    3: "DUMMY",
    4: "MOD_CONFUSE",
    7: "MOD_FEAR",
    8: "PERIODIC_HEAL (HoT)",
    12: "MOD_STUN",
    13: "MOD_DAMAGE_DONE",
    15: "DAMAGE_SHIELD",
    16: "MOD_STEALTH",
    18: "MOD_INVISIBILITY",
    22: "MOD_RESISTANCE",
    23: "PERIODIC_TRIGGER_SPELL",
    24: "PERIODIC_ENERGIZE",
    27: "PERIODIC_LEECH",
    29: "MOD_STAT",
    31: "MOD_INCREASE_SPEED",
    33: "MOD_DECREASE_SPEED",
    50: "PROC_TRIGGER_SPELL",
    53: "PERIODIC_DAMAGE_PERCENT",
    54: "DUMMY_2",
    65: "MELEE_SLOW",
    78: "PERIODIC_TRIGGER_SPELL_WITH_VALUE",
    79: "MOD_DAMAGE_PERCENT_DONE",
    99: "MOD_ATTACK_POWER",
    107: "ADD_FLAT_MODIFIER (SPELLMOD)",
    108: "ADD_PCT_MODIFIER (SPELLMOD)",
    158: "MOD_HEALING_DONE",
    184: "MOD_ATTACKER_MELEE_HIT_CHANCE",
    188: "MOD_ATTACKER_HIT_CHANCE",
    216: "MOD_CASTING_SPEED",
    226: "PERIODIC_DUMMY",
}


# ─── Рендеры ────────────────────────────────────────────────────────────────

@dataclass
class SpellRow:
    """Срез spell_template, нужный для построения тикета.

    DurationIndex — ссылка на SpellDuration.dbc; самой длительности в spell_template нет.
    Description / Duration описание реально читается клиентом из Spell.dbc; для тикета не критично,
    в Phase E будем грузить тултип через web-эндпоинт, где есть полноценный SpellInfo.
    """
    Id: int
    SpellName: str
    SpellLevel: int
    SpellFamilyName: int
    SchoolMask: int
    ManaCost: int
    PowerType: int
    Effect1: int
    EffectBasePoints1: int
    EffectDieSides1: int
    EffectAura1: int
    Effect2: int
    EffectBasePoints2: int
    Effect3: int
    EffectBasePoints3: int
    RecoveryTime: int
    DurationIndex: int


def school_name(mask: int) -> str:
    """Чтение SchoolMask: либо одна школа из таблицы, либо комбинация через слэш, либо #N."""
    if mask in SCHOOL_MASK:
        return SCHOOL_MASK[mask]
    parts = [name for bit, name in SCHOOL_MASK.items() if mask & bit]
    return " / ".join(parts) if parts else f"#{mask}"


def power_type(pt: int) -> str:
    return POWER_TYPE.get(pt, f"power#{pt}")


def family_name(fam: int) -> str:
    return FAMILY_NAME.get(fam, f"family#{fam}")


def effect_name(eff: int) -> str:
    return EFFECT_NAME.get(eff, f"#{eff}")


def aura_name(aura: int) -> str:
    return AURA_NAME.get(aura, f"#{aura}")


def target_hint(eff: int) -> str:
    """По типу первого эффекта подсказать тестеру какую цель брать."""
    if eff in (2, 30, 35, 38, 80):  # damage-like
        return "тренировочный манекен или враг"
    if eff in (10,):  # heal
        return "союзник или вы сами"
    if eff in (6, 8) or eff == 0:
        return "вы сами (если бафф) или союзник / враг — по описанию"
    if eff == 24:
        return "не нужна — создаётся предмет"
    return "по описанию ниже"


def pick_priority(row: SpellRow) -> str:
    """Эвристика приоритета. Можно перебалансировать без переноса данных."""
    # Базовый урон / хил / восстановление ресурса — Blocker
    if row.Effect1 in (2, 8, 10, 80) and row.SpellLevel < 20:
        return "Blocker"
    # Класс-семейства с активным эффектом — Major
    if row.SpellFamilyName != 0 and row.Effect1 in (2, 6, 8, 10, 64, 77, 80):
        return "Major"
    # Пассивы и общие — Minor
    return "Minor"


def render_description(row: SpellRow) -> str:
    cls = family_name(row.SpellFamilyName)
    school = school_name(row.SchoolMask)
    eff1 = effect_name(row.Effect1)
    eff2 = effect_name(row.Effect2) if row.Effect2 else None
    eff3 = effect_name(row.Effect3) if row.Effect3 else None
    aura = aura_name(row.EffectAura1) if row.EffectAura1 else None

    lines = [f"**{cls} — {school}**", ""]
    lines.append(f"Эффект 1: {eff1}" + (f" · Aura: {aura}" if aura else ""))
    if eff2:
        lines.append(f"Эффект 2: {eff2}")
    if eff3:
        lines.append(f"Эффект 3: {eff3}")
    lines.append("")
    lines.append(f"Spell ID: {row.Id} · уровень изучения: {row.SpellLevel}")
    lines.append(f"Стоимость: {row.ManaCost} {power_type(row.PowerType)}")
    if row.RecoveryTime:
        lines.append(f"Восстановление: {row.RecoveryTime} мс")
    if row.DurationIndex:
        lines.append(f"Длительность: SpellDuration.dbc index = {row.DurationIndex} (значение в клиенте)")
    lines.append("")
    lines.append(f"Wowhead WotLK: https://wotlkdb.com/?spell={row.Id}")
    lines.append("Источник: spell_template (mangos) — сгенерировано tools/regression-import/")
    return "\n".join(lines)


def render_test_steps(row: SpellRow) -> str:
    cls = family_name(row.SpellFamilyName)
    return "\n".join([
        f"1. Войти тестировщиком: класс «{cls}», уровень ≥ {max(row.SpellLevel, 1)}.",
        f"2. Изучить «{row.SpellName}» (spell id: {row.Id}) — у тренера класса или через `.learn {row.Id}`.",
        f"3. Поставить способность на панель действий. Цель: {target_hint(row.Effect1)}.",
        "4. Применить способность; зафиксировать наблюдаемый эффект (см. «Ожидаемый результат»).",
    ])


def render_expected(row: SpellRow) -> str:
    school = school_name(row.SchoolMask)
    eff1 = effect_name(row.Effect1)
    aura = aura_name(row.EffectAura1) if row.EffectAura1 else None
    pt = power_type(row.PowerType)

    base = row.EffectBasePoints1 + 1  # CMaNGOS-конвенция: BasePoints хранится со смещением -1
    die = row.EffectDieSides1

    parts = [
        f"* Способность применяется без ошибок (нет «invalid target»/«out of range»/«not enough mana»).",
        f"* Эффект: {eff1}" + (f" · Aura: {aura}" if aura else "") + f"; школа: {school}.",
        f"* Стоимость: {row.ManaCost} {pt}.",
    ]
    if base or die:
        if die > 1:
            parts.append(f"* Базовая величина: {base}–{base + die - 1} (BasePoints={row.EffectBasePoints1}, DieSides={die}).")
        else:
            parts.append(f"* Базовая величина: {base} (BasePoints={row.EffectBasePoints1}).")
    if row.RecoveryTime:
        parts.append(f"* Восстановление: {row.RecoveryTime} мс.")
    parts.append(f"* Эталон: https://wotlkdb.com/?spell={row.Id} (сверить с CMaNGOS spell_template).")
    return "\n".join(parts)


def render_title(row: SpellRow) -> str:
    """Формат `#{spell_id} · {SpellName}` — этот же regex парсит спелл-id в /Ticket для preview-блока (Phase E)."""
    return f"#{row.Id} · {row.SpellName}"


def family_label(fam: int) -> str:
    """Метка класса в нижнем регистре для фильтрации на доске."""
    return {
        0: "generic", 3: "mage", 4: "warrior", 5: "warlock",
        6: "priest", 7: "druid", 8: "rogue", 9: "hunter",
        10: "paladin", 11: "shaman", 15: "deathknight",
    }.get(fam, "generic")


def build_ticket(row: SpellRow, epic_id: int) -> dict:
    return {
        "title": render_title(row),
        "type": "Task",
        "priority": pick_priority(row),
        "epicId": epic_id,
        "description": render_description(row),
        "testSteps": render_test_steps(row),
        "expectedResult": render_expected(row),
        "assignee": "Агент ИИ",
        "labels": ["regression", family_label(row.SpellFamilyName)],
    }
