namespace AlexWoW.Database.Entities;

/// <summary>EF-сущность таблицы <c>characters</c> (БД alexwow_auth). Срез 2 рефактора DAL (#23).</summary>
public sealed class Character
{
    public uint Guid { get; set; }
    public uint AccountId { get; set; }
    public string Name { get; set; } = null!;
    public byte Race { get; set; }
    public byte Class { get; set; }
    public byte Gender { get; set; }
    public byte Skin { get; set; }
    public byte Face { get; set; }
    public byte HairStyle { get; set; }
    public byte HairColor { get; set; }
    public byte FacialHair { get; set; }
    public byte Level { get; set; }          // DEFAULT 1
    public uint Zone { get; set; }            // DEFAULT 0
    public uint Map { get; set; }             // DEFAULT 0
    public float PositionX { get; set; }      // DEFAULT 0
    public float PositionY { get; set; }      // DEFAULT 0
    public float PositionZ { get; set; }      // DEFAULT 0
    public DateTime CreatedAt { get; set; }   // timestamp DEFAULT CURRENT_TIMESTAMP
    public uint Money { get; set; }           // DEFAULT 1000000
    public uint Xp { get; set; }              // DEFAULT 0
    public byte ActionBars { get; set; }      // DEFAULT 0
    public uint TalentResetCost { get; set; } // последняя стоимость сброса талантов (медь); DEFAULT 0. M9.8
    public bool IsTester { get; set; }        // KB6: персонаж-тестировщик QA-доски; DEFAULT 0

    // KB#87: soft-delete. Никогда не удаляем row физически — иначе AUTO_INCREMENT
    // может вернуть тот же guid, и клиент по своему WDB-кэшу подтянет данные старого
    // персонажа → access violation в рендере на char-select.
    public DateTime? DeletedAt { get; set; }
}
