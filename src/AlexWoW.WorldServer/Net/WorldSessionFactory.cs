using System.Net.Sockets;

namespace AlexWoW.WorldServer.Net;

/// <summary>
/// Фабрика world-сессий (M7 #35): объект с ручным временем жизни (per-connection) создаётся
/// из DI-собранного <see cref="WorldSessionServices"/> — слушателю не нужно знать зависимости сессии.
/// </summary>
internal sealed class WorldSessionFactory(WorldSessionServices services)
{
    public WorldSession Create(Socket socket) => new(socket, services);
}
