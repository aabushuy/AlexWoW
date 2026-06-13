namespace AlexWoW.WorldServer.World;

/// <summary>
/// Множитель «% наносимого урона» (Фаза 2): сумма активных аур кастера с <see cref="ActiveAura.DamageDonePct"/>,
/// совпадающих по маске школ (<see cref="ActiveAura.DamageDoneSchool"/> = 0 — все школы). Источник — MOD_DAMAGE_PERCENT_DONE
/// (Shadowform +15% Shadow, Avenging Wrath +20% all). Применяется к прямому урону спелла и к тикам DoT.
/// </summary>
internal static class DamageDoneModifier
{
    /// <summary>Возвращает <paramref name="amount"/>, умноженный на суммарный % урона по школе <paramref name="school"/>.</summary>
    public static int Apply(Net.WorldSession session, byte school, int amount)
    {
        var pct = 0;
        foreach (var a in session.Progression.Auras)
            if (a.DamageDonePct != 0 && (a.DamageDoneSchool == 0 || (a.DamageDoneSchool & school) != 0))
                pct += a.DamageDonePct;
        return pct != 0 ? amount * (100 + pct) / 100 : amount;
    }
}
