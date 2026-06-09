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

        var spells = (await db.QueryAsync<TrainerSpell>(new CommandDefinition("""
            SELECT spell AS Spell, spellcost AS SpellCost, reqskill AS ReqSkill,
                   reqskillvalue AS ReqSkillValue, reqlevel AS ReqLevel,
                   COALESCE(ReqAbility1, 0) AS ReqAbility1, COALESCE(ReqAbility2, 0) AS ReqAbility2,
                   COALESCE(ReqAbility3, 0) AS ReqAbility3
            FROM npc_trainer WHERE entry = @entry
            UNION
            SELECT spell, spellcost, reqskill, reqskillvalue, reqlevel,
                   COALESCE(ReqAbility1, 0), COALESCE(ReqAbility2, 0), COALESCE(ReqAbility3, 0)
            FROM npc_trainer_template WHERE @templateId <> 0 AND entry = @templateId;
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
}
