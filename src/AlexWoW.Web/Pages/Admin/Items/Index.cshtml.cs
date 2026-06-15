using AlexWoW.Database.Abstractions;
using AlexWoW.Database.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AlexWoW.Web.Pages.Admin.Items;

/// <summary>
/// Поиск предметов по item_template (админ). Настраиваемый фильтр (требуемый уровень/класс/тип/фраза),
/// таблица с подсветкой по качеству и тултипом при наведении. Запрос — только при заполненном фильтре.
/// </summary>
public sealed class IndexModel(IItemSearchRepository items) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public uint? LevelMin { get; set; }

    [BindProperty(SupportsGet = true)]
    public uint? LevelMax { get; set; }

    [BindProperty(SupportsGet = true)]
    public byte? PlayerClass { get; set; }

    [BindProperty(SupportsGet = true)]
    public ItemKind? Kind { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Name { get; set; }

    public IReadOnlyList<ItemTemplateData> Results { get; private set; } = [];

    /// <summary>Был ли выполнен поиск (хоть один фильтр заполнен) — чтобы отличить «пусто» от «не искали».</summary>
    public bool Searched { get; private set; }

    public const int Limit = 100;

    public async Task OnGetAsync(CancellationToken ct)
    {
        Searched = LevelMin.HasValue || LevelMax.HasValue || PlayerClass.HasValue
            || Kind.HasValue || !string.IsNullOrWhiteSpace(Name);
        if (!Searched)
            return;

        var filter = new ItemSearchFilter
        {
            LevelMin = LevelMin,
            LevelMax = LevelMax,
            PlayerClass = PlayerClass,
            Kind = Kind,
            NameContains = Name,
            Limit = Limit,
        };
        Results = await items.SearchAsync(filter, ct);
    }
}
