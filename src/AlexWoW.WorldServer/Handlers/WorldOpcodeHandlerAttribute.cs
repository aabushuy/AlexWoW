using AlexWoW.WorldServer.Protocol;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Помечает метод-обработчик опкода в модуле <see cref="IOpcodeHandlerModule"/>. Сигнатура:
/// <c>public Task Method(WorldSession, IncomingPacket, CancellationToken)</c>.
/// Один метод может обслуживать несколько опкодов (например, все MSG_MOVE_*).
/// (Мост M7 #35: до конца конверсии атрибут работает и на легаси-статических методах.)
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
internal sealed class WorldOpcodeHandlerAttribute(params WorldOpcode[] opcodes) : Attribute
{
    public WorldOpcode[] Opcodes { get; } = opcodes;
}
