namespace AlexWoW.Database.Entities;

/// <summary>
/// EF-сущность таблицы <c>dev_teleport</c> (БД alexwow_auth) — точки телепорта для dev-меню аддона
/// (столицы Альянса/Орды и нейтральные города). Редактируется в рантайме (координаты/порядок/состав),
/// аддон тянет список из БД. Devcommands (#79).
/// </summary>
public sealed class TeleportLocation
{
    public uint Id { get; set; }            // PK (auto-increment)
    public int SortOrder { get; set; }      // порядок в меню (по возрастанию)
    public string Name { get; set; } = null!; // отображаемое имя города
    public byte Faction { get; set; }       // 0 = нейтрал/обе, 1 = Альянс, 2 = Орда
    public uint Map { get; set; }           // карта (0=Вост.королевства, 1=Калимдор, 530=Запределье, 571=Нордскол)
    public uint Zone { get; set; }          // зона (информативно; 0 — не задана)
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float O { get; set; }            // ориентация (радианы)
}
