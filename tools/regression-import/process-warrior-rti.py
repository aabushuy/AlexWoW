#!/usr/bin/env python3
"""KB14: пакетная обработка regression-тикетов Воина (project 650) из колонки Ready to Implementation.

Запускать на homeserver. Идемпотентно по статусу. Комментарии добавляются всегда — повторный прогон
обогащает историю; запускать повторно только при изменении формулировок.
"""
import json
import os
import subprocess
import sys


def api_token() -> str:
    with open("/data/docker/alexwow-config/.env") as f:
        for line in f:
            if line.startswith("WEB_API_TOKEN="):
                return line.strip().split("=", 1)[1]
    raise RuntimeError("WEB_API_TOKEN не найден")


def call(method: str, path: str, body: dict) -> dict:
    cmd = [
        "curl", "-s", "-X", method,
        "-H", f"X-Api-Token: {TOKEN}",
        "-H", "Content-Type: application/json",
        "--data", json.dumps(body, ensure_ascii=False),
        f"http://localhost:8090/api/kanban{path}",
    ]
    out = subprocess.run(cmd, capture_output=True, text=True, check=False)
    if out.returncode != 0 or not out.stdout:
        print(f"!! {method} {path}: rc={out.returncode} err={out.stderr.strip()}", file=sys.stderr)
    return json.loads(out.stdout) if out.stdout else {}


def comment(ticket_id: int, body: str):
    call("POST", f"/tickets/{ticket_id}/comments", {"author": "claude", "body": body})


def move(ticket_id: int, status: str):
    call("POST", f"/tickets/{ticket_id}/move", {"status": status})


# ─── Реестры ────────────────────────────────────────────────────────────────

READY_TO_TEST = {
    2014: "Battle Stance (#2457). Стойка — AuraService применяет shapeshift form через UNIT_FIELD_BYTES_2 (M6.12). Иконка APPLY_AURA #36 видна, кулдаун кнопки сбрасывается через SMSG_COOLDOWN_EVENT.",
    2015: "Victory Rush (#34428). Effect=SCHOOL_DAMAGE — обработка через SpellEffectsService.ApplyDamageAsync. Без специфических условий (триггер по победе в бою) — на манекене кастуется напрямую.",
    2017: "Defensive Stance (#71). Стойка — AuraService, form=2.",
    2021: "Mocking Blow (#694). Effect1 weapon damage + Effect2 APPLY_AURA #11 (taunt). Урон через SpellEffectsService, дебафф-иконка через PeriodicsService.ApplyAuraEffectAsync (ветка дебаффа на существо). Реальный таунт (переключение цели моба) вне scope: threat-система в проекте пока не реализована — это будет видно только как иконка ауры.",
    2023: "Disarm (#676). Три APPLY_AURA (Aura #67/87/278) — дебафф на цель. PeriodicsService.ApplyAuraEffectAsync рисует все иконки. Реальная блокировка автоатаки моба (disarm-механика) пока не моделируется — это вне scope визуальной проверки.",
    2027: "Challenging Shout (#1161). AoE-taunt (APPLY_AURA #11) с площадной целью. Без threat-системы фактического таунта нет, но иконка ауры рисуется.",
    2029: "Berserker Stance (#2458). Стойка form=3.",
    2030: "Intercept (#20252). Effect=CHARGE — обрабатывается SpellEffectsService.ApplyChargeAsync (M7 #33, сплайн-рывок). EffectTriggerSpell — сабспелл (стан) накладывается следом.",
    2031: "Berserker Rage (#18499). Self-buff с MECHANIC_IMMUNITY (Aura #77). В SpellCatalog есть special-case (line ~475): auraPositive=true несмотря на Bp=-1. PeriodicsService.ApplyAuraEffectAsync применяет как положительный самобафф; иконка #77 видна 10s.",
    2032: "Whirlwind (#1680). Effect=WEAPON_DAMAGE_NOSCHOOL + EffectTriggerSpell. Прямой каст работает, урон применяется. Реальный AoE-сбор целей вокруг — текущим движком ограничен (без полного area-target по навмешу), но базовое поведение касто-эффекта работает.",
    2035: "Recklessness (#1719). Три APPLY_AURA само-баффа (SPELLMOD, +damage taken, MECHANIC_IMMUNITY). PeriodicsService.ApplyAuraEffectAsync рисует самобафф. Иконка ауры + длительность 12s видны.",
    2036: "Spell Reflection (#23920). APPLY_AURA #28 (MOD_REFLECT_SPELLS_SCHOOL), Bp=99 → auraPositive=true, самобафф. PeriodicsService рисует. Фактическое отражение спеллов вне scope — это серверная механика, которая будет добавлена отдельно; текущий тест проверяет каст + иконку.",
    2038: "Shattering Throw (#64382). Effect=SCHOOL_DAMAGE + APPLY_AURA #101 (MOD_RESISTANCE). Прямой урон + дебафф −% брони на цели. Урон через SpellEffectsService, дебафф через PeriodicsService.",
    2039: "Enraged Regeneration (#55694). HoT (Aura #20) на себя + MECHANIC_IMMUNITY. PeriodicsService.ApplyAsync обрабатывает HoT (10s, 5 тиков), AuraService рисует иконку. M6.5 / M6.11 покрывают.",
    2041: "Rend (#47465). DoT (Aura #3 PERIODIC_DAMAGE). PeriodicsService.ApplyAsync — стандартный путь периодического урона. Иконка + тики каждые 3s в течение 15s.",
    2044: "Thunder Clap (#47502). AoE direct damage (Effect=SCHOOL_DAMAGE) + APPLY_AURA #138 (slow attack). Урон через SpellEffectsService, дебафф через PeriodicsService.",
    2045: "Demoralizing Shout (#47437). AoE дебафф −AP (Aura #99). Площадной таргет, дебафф-иконка рисуется на целях.",
    2047: "Commanding Shout (#47440). Self/Party-бафф +МАХ HP (Aura #230). Аналог Battle Shout — auraPositive=true, PeriodicsService рисует. Прибавка реального HP моделируется через HealthBonus (M10.4c).",
    2052: "Heroic Throw (#57755). Effect=SCHOOL_DAMAGE — прямой бросок щитом, дальний радиус. Стандартная обработка через SpellEffectsService.",
}

NEEDS_WORK = {
    2020: "Overpower (#7384). Требует серверный условный триггер «после уклонения цели». В клиенте кнопка серая до момента dodge; чтобы тест прошёл — нужен серверный SpellMod на 5s окно после события dodge цели. Не покрыто текущим движком; нужна доработка SpellEffectsService с emission события на dodge.",
    2024: "Stance Mastery (#12678). Пассивный талант: +2 rage при смене стойки. Триггерная пассивка требует хука в AuraService (на момент применения shapeshift form) с начислением rage через CombatResourcesService. В текущем коде нет такого хука.",
    2025: "Retaliation (#20230). Талант: на 15s контратака при каждом ударе по нам. Триггерная аура, при melee hit by enemy — мгновенный ответный удар без расхода ярости. Требует хук в incoming-damage пайплайне (PlayerMeleeService/CreatureCombatAI) + триггер контратаки. Не реализовано.",
    2040: "Heroic Strike (#47450). On-next-swing (Effect=58). Клиент ставит запрос в очередь до следующего AutoAttack, сервер должен заменить мили-удар на буст-удар. В текущем PlayerMeleeService этой ветки нет.",
    2042: "Cleave (#47520). On-next-swing AoE (Effect=58). Та же проблема что Heroic Strike + дополнительный таргет. Требует расширения PlayerMeleeService для on-next-swing + AoE-выборки соседей.",
    2046: "Slam (#47475). Effect=DUMMY с cast-time 1.5s, по факту бэк-эффект — мили урон ×2 + 250 bonus. Dummy-скрипт без serverside-обработки сейчас не наносит урон. Нужен скрипт в SpellEffectsService для Slam (заменить Dummy на NormalizedWeaponDamage+Bonus).",
    2048: "Execute (#47471). Effect=DUMMY: удар по цели <20% HP, потребляет всю ярость, +урон за каждую сверх 10. Текущий Dummy не наносит урон + не модифицирует ярость + нет HP-условия. Нужен серверный скрипт.",
    2051: "Devastate (#47498). Effect=NORMALIZED_WEAPON_DAMAGE + Effect=WEAPON_DAMAGE_NOSCHOOL + Effect=TRIGGER (apply Sunder Armor stack). Урон работает, но дебафф-стак Sunder Armor (механика accumulating-stack ауры) сейчас не моделируется в PeriodicsService — каждый каст рисует независимую иконку без счётчика.",
}


# ─── Запуск ─────────────────────────────────────────────────────────────────

TOKEN = api_token()

for tid, text in READY_TO_TEST.items():
    comment(tid, "Готово к проверке в клиенте. " + text)
    move(tid, "Testing")
    print(f"[Testing] {tid}")

for tid, text in NEEDS_WORK.items():
    comment(tid, "Требуется доработка кода — оставляю в In Progress. " + text)
    move(tid, "In Progress")
    print(f"[In Progress] {tid}")

print(f"Done. Testing={len(READY_TO_TEST)} / In Progress={len(NEEDS_WORK)}")
