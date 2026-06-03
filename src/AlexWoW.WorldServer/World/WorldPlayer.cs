using AlexWoW.Database.Models;
using AlexWoW.WorldServer.Net;

namespace AlexWoW.WorldServer.World;

/// <summary>
/// Игрок, находящийся в мире: данные персонажа + ссылка на сессию (для отправки пакетов).
/// Живые координаты читаются из сессии (туда их пишет трекинг движения), карта — из персонажа
/// (телепорта между картами пока нет).
/// </summary>
public sealed class WorldPlayer
{
    public required ulong Guid { get; init; }
    public required Character Character { get; init; }
    public required WorldSession Session { get; init; }

    public uint Map => Character.Map;
    public float X => Session.PosX;
    public float Y => Session.PosY;
    public float Z => Session.PosZ;
    public float O => Session.PosO;
}
