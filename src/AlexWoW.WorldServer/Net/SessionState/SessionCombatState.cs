namespace AlexWoW.WorldServer.Net.SessionState;

/// <summary>
/// Боевое состояние сессии: цель, авторитетный HP, мили-свинг, боевые ресурсы (ярость/энергия),
/// параметры оружия. Выделено из плоских полей <see cref="WorldSession"/>. M7 S9 #43.
/// </summary>
internal sealed class SessionCombatState
{
    /// <summary>Текущая цель (CMSG_SET_SELECTION). 0 — нет. M6.3.</summary>
    internal ulong SelectionGuid { get; set; }

    /// <summary>GUID существа, по которому идёт авто-атака (0 — не в бою). Читается тиком. M6.3.</summary>
    internal ulong CombatTargetGuid { get; set; }

    /// <summary>Время следующего мили-свинга (<see cref="Environment.TickCount64"/>, мс). M6.3.</summary>
    internal long NextMeleeSwingMs { get; set; }

    /// <summary>Послали ли клиенту «вне радиуса» для текущего эпизода (анти-спам). M6.3.</summary>
    internal bool MeleeNotInRangeNotified { get; set; }

    /// <summary>Авторитетное здоровье игрока (UNIT_FIELD_HEALTH). Меняется уроном существ. M6.7.</summary>
    internal uint Health { get; set; }
    internal uint MaxHealth { get; set; }

    /// <summary>Время последней боевой активности (нанёс/получил урон) — для внебоевого регена HP. M6.7.</summary>
    internal long LastCombatMs { get; set; }
    /// <summary>Время последнего тика регена HP (кадэнс 1 с). M6.7.</summary>
    internal long LastHealthRegenMs { get; set; }
    /// <summary>Игрок мёртв (HP=0, ждёт release/возрождения). M6.7.</summary>
    internal bool IsDead { get; set; }

    // --- Боевые ресурсы: ярость/энергия (M6.12) ---
    /// <summary>Ярость воина/друида (UNIT_FIELD_POWER1+1). Хранится ×10 (0..1000 = 0..100 у клиента).
    /// Копится от мили-урона, распадается вне боя. 0 у не-ярость-классов. M6.12.</summary>
    internal uint Rage { get; set; }
    /// <summary>Энергия разбойника (UNIT_FIELD_POWER1+3), 0..100. Реген ~постоянный. M6.12.</summary>
    internal uint Energy { get; set; }
    /// <summary>Скорость оружия главной руки (мс) — для формулы ярости. Ставится в RefreshMeleeAsync. M6.12.</summary>
    internal uint MainHandSpeedMs { get; set; } = 2000;
    /// <summary>Урон оружия главной руки (min/max) — для мили-абилок (WEAPON_DAMAGE). RefreshMeleeAsync. M10.4a.</summary>
    internal float WeaponMinDamage { get; set; } = 1f;
    internal float WeaponMaxDamage { get; set; } = 2f;
    /// <summary>Время последнего тика ресурса (реген энергии / распад ярости, кадэнс 1 с). M6.12.</summary>
    internal long LastResourceTickMs { get; set; }

    /// <summary>Сброс при выходе из мира — только то, что сбрасывалось в LeaveWorld и раньше
    /// (HP/ресурсы/тайминги переживают выход by design — переинициализируются при входе).</summary>
    internal void Reset()
    {
        CombatTargetGuid = 0; // M6.3: вне мира боя нет
        SelectionGuid = 0;
        IsDead = false;       // M6.7: боевое/жизненное состояние сбрасывается при выходе
    }
}
