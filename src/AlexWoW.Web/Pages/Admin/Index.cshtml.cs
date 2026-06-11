using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Models;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AlexWoW.Web.Pages.Admin;

/// <summary>Список сессий захвата проверки заклинаний (M12 Spell QA, только для админов).</summary>
public sealed class IndexModel(ISpellTestRepository spellTest) : PageModel
{
    public IReadOnlyList<SpellTestSession> Sessions { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct)
        => Sessions = await spellTest.GetSessionsAsync(200, ct);
}
