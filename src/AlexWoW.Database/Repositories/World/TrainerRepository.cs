using System.Globalization;
using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Models;
using Dapper;

namespace AlexWoW.Database.Repositories.World;

/// <summary>Тренеры БД мира (npc_trainer[_template] + creature_template). SRP-репозиторий (#25).</summary>
public sealed class TrainerRepository(string connectionString)
    : MangosRepositoryBase(connectionString), ITrainerRepository
{
    public async Task<TrainerData?> GetTrainerAsync(uint entry, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);

        var head = await db.QuerySingleOrDefaultAsync(new CommandDefinition(
            "SELECT TrainerType, TrainerClass, TrainerRace, TrainerTemplateId FROM creature_template WHERE Entry = @entry;",
            new { entry }, cancellationToken: ct));
        if (head is null)
            return null;
        var h = (IDictionary<string, object>)head;
        byte trainerType = Convert.ToByte(h["TrainerType"], CultureInfo.InvariantCulture);
        byte trainerClass = Convert.ToByte(h["TrainerClass"], CultureInfo.InvariantCulture);
        byte trainerRace = Convert.ToByte(h["TrainerRace"], CultureInfo.InvariantCulture);
        uint templateId = Convert.ToUInt32(h["TrainerTemplateId"], CultureInfo.InvariantCulture);

        // SpellLevel из spell_template (дамп Spell.dbc) — реальный уровень изучения ранга: в дампе
        // npc_trainer.reqlevel почти всегда 0, поэтому гейт ранга по уровню без этого не работает (#27).
        var spells = (await db.QueryAsync<TrainerSpell>(new CommandDefinition("""
            SELECT u.spell AS Spell, u.spellcost AS SpellCost, u.reqskill AS ReqSkill,
                   u.reqskillvalue AS ReqSkillValue, u.reqlevel AS ReqLevel,
                   u.ReqAbility1 AS ReqAbility1, u.ReqAbility2 AS ReqAbility2, u.ReqAbility3 AS ReqAbility3,
                   COALESCE(st.SpellLevel, 0) AS SpellLevel
            FROM (
                SELECT spell, spellcost, reqskill, reqskillvalue, reqlevel,
                       COALESCE(ReqAbility1, 0) ReqAbility1, COALESCE(ReqAbility2, 0) ReqAbility2,
                       COALESCE(ReqAbility3, 0) ReqAbility3
                FROM npc_trainer WHERE entry = @entry
                UNION
                SELECT spell, spellcost, reqskill, reqskillvalue, reqlevel,
                       COALESCE(ReqAbility1, 0), COALESCE(ReqAbility2, 0), COALESCE(ReqAbility3, 0)
                FROM npc_trainer_template WHERE @templateId <> 0 AND entry = @templateId
            ) u
            LEFT JOIN spell_template st ON st.Id = u.spell;
            """, new { entry, templateId }, cancellationToken: ct))).AsList();

        if (trainerType == 0 && spells.Count == 0)
            return null; // не тренер

        var greeting = await db.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT Text FROM trainer_greeting WHERE Entry = @entry;", new { entry }, cancellationToken: ct));

        return new TrainerData
        {
            TrainerType = trainerType, TrainerClass = trainerClass, TrainerRace = trainerRace,
            Greeting = greeting ?? string.Empty, Spells = spells,
        };
    }

    public async Task<uint?> GetClassTrainerEntryAsync(byte classId, CancellationToken ct = default)
    {
        await using var db = await OpenAsync(ct);

        // Только тренеры класса с ПРЯМЫМ npc_trainer (наши дев-тренеры Faction=35 кладут спеллы напрямую;
        // у канонических городских тренеров спеллы в npc_trainer_template — их сюда не берём, чтобы не
        // спавнить тренера чужой фракции, который для игрока был бы враждебным). Предпочтения:
        // 1) дружелюбный ко всем (Faction=35), 2) зарезервированный дев-диапазон, 3) самый полный набор.
        return await db.QuerySingleOrDefaultAsync<uint?>(new CommandDefinition("""
            SELECT ct.Entry
            FROM creature_template ct
            WHERE ct.TrainerType = 0 AND ct.TrainerClass = @classId
              AND EXISTS (SELECT 1 FROM npc_trainer nt WHERE nt.entry = ct.Entry)
            ORDER BY (ct.Faction = 35) DESC,
                     (ct.Entry BETWEEN 990001 AND 990099) DESC,
                     (SELECT COUNT(*) FROM npc_trainer nt WHERE nt.entry = ct.Entry) DESC,
                     ct.Entry ASC
            LIMIT 1;
            """, new { classId }, cancellationToken: ct));
    }
}
