using AlexWoW.WorldServer.Protocol;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Помечает статический метод-обработчик опкода. Сигнатура метода:
/// <c>public static Task Method(WorldSession, IncomingPacket, CancellationToken)</c>.
/// Один метод может обслуживать несколько опкодов (например, все MSG_MOVE_*).
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
internal sealed class WorldOpcodeHandlerAttribute(params WorldOpcode[] opcodes) : Attribute
{
    public WorldOpcode[] Opcodes { get; } = opcodes;
}
