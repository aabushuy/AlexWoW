using System.Reflection;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

internal delegate Task WorldOpcodeHandler(WorldSession session, IncomingPacket packet, CancellationToken ct);

/// <summary>
/// Реестр обработчиков опкодов. Таблица <c>opcode → handler</c> строится один раз через
/// рефлексию из всех статических методов, помеченных <see cref="WorldOpcodeHandlerAttribute"/>.
/// Добавить опкод = добавить метод с атрибутом; центральный switch не нужен.
/// </summary>
internal static class WorldPacketRouter
{
    private static readonly Dictionary<WorldOpcode, WorldOpcodeHandler> Handlers = BuildHandlerTable();

    /// <summary>Число зарегистрированных опкодов (для диагностики при старте).</summary>
    public static int HandlerCount => Handlers.Count;

    public static async Task DispatchAsync(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        if (Handlers.TryGetValue(packet.Opcode, out var handler))
            await handler(session, packet, ct);
        else
            session.Logger.LogInformation("Опкод {Opcode} (0x{Value:X}) от {Ip} — без обработчика",
                packet.Opcode, (uint)packet.Opcode, session.RemoteIp);
    }

    private static Dictionary<WorldOpcode, WorldOpcodeHandler> BuildHandlerTable()
    {
        var table = new Dictionary<WorldOpcode, WorldOpcodeHandler>();

        var methods = typeof(WorldPacketRouter).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static));

        foreach (var method in methods)
        {
            var attributes = method.GetCustomAttributes<WorldOpcodeHandlerAttribute>().ToArray();
            if (attributes.Length == 0)
                continue;

            var handler = (WorldOpcodeHandler)Delegate.CreateDelegate(typeof(WorldOpcodeHandler), method);
            foreach (var attribute in attributes)
                foreach (var opcode in attribute.Opcodes)
                    table[opcode] = handler;
        }

        return table;
    }
}
