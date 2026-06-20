using AlexWoW.WorldServer.Net;

namespace AlexWoW.WorldServer.Handlers.Spells.Druid;

/// <summary>
/// Друид — Природная реакция (Natural Reaction, 57878/57881). Талант в дереве Зверя: при УКЛОНЕНИИ от
/// удара в форме медведя/исполинского медведя восстанавливает 1/3 ярости. Аура-дискриминатор по форме
/// и по procEx=Dodge. Эталон CMaNGOS HandleDummyAuraProc case 57878.
/// </summary>
[DummyAuraHandler(57878, 57881)]
internal sealed class NaturalReactionHandler(CombatResourcesService combatResources) : DummyAuraHandlerBase
{
    /// <summary>Формы медведя/исполинского медведя — таблица CMaNGOS shapeshift forms.</summary>
    private const byte BearForm = 1;
    private const byte DireBearForm = 8;

    /// <summary>Ярость на dodge — ранг 1: 1, ранг 2: 3 (×10 в нашей нотации).</summary>
    private static readonly Dictionary<uint, uint> RagePerRankX10 = new()
    {
        [57878] = 10, // rank 1: 1 rage
        [57881] = 30, // rank 2: 3 rage
    };

    public override async Task<bool> OnProcAsync(WorldSession session, uint spellId,
        DummyProcContext ctx, CancellationToken ct)
    {
        // Только событие dodge (victim-side procEx из CreatureCombatAI).
        if ((ctx.ProcEx & ProcFlagEx.Dodge) == 0)
            return false;
        // Только в формах медведя/исполинского медведя (бафф пассивно даёт +dodge, бонусная ярость — только в форме).
        var form = session.Progression.ShapeshiftForm;
        if (form != BearForm && form != DireBearForm)
            return false;
        if (!RagePerRankX10.TryGetValue(spellId, out var rageX10))
            return false;
        await combatResources.AddRageAsync(session, rageX10, ct);
        return true; // съели событие — generic-триггер не нужен (у этого таланта нет ProcTriggerSpellId)
    }
}
