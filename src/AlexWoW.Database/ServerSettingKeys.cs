namespace AlexWoW.Database;

/// <summary>
/// Известные ключи таблицы <c>server_setting</c> и их значения по умолчанию. Сидируются
/// идемпотентно при старте AuthServer (<c>AuthSchemaInitializer</c>); правятся в рантайме.
/// </summary>
public static class ServerSettingKeys
{
    /// <summary>Стоимость смены расы персонажа игроком (в золоте). M8.6.</summary>
    public const string RaceChangeCostGold = "cost.race_change_gold";

    /// <summary>Стоимость смены пола персонажа игроком (в золоте). M8.6.</summary>
    public const string GenderChangeCostGold = "cost.gender_change_gold";

    /// <summary>Значения по умолчанию (ключ → значение) для идемпотентного сида.</summary>
    public static readonly IReadOnlyDictionary<string, string> Defaults = new Dictionary<string, string>
    {
        [RaceChangeCostGold] = "1000",
        [GenderChangeCostGold] = "2000",
    };
}
