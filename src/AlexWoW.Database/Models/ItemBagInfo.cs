namespace AlexWoW.Database.Models;

/// <summary>
/// Минимум из item_template для раскладки инвентаря/сумок (M6.13): класс предмета (1 = контейнер/сумка),
/// число слотов контейнера и макс. прочность. Батч-загружается по entry'ям инвентаря при входе и кэшируется
/// на сессии — чтобы перемещения/выдача не дёргали БД мира на каждый предмет.
/// </summary>
public readonly record struct ItemBagInfo(uint Class, uint ContainerSlots, uint MaxDurability)
{
    /// <summary>Предмет — контейнер (item_template.class = 1, ITEM_CLASS_CONTAINER).</summary>
    public bool IsContainer => Class == 1;
}
