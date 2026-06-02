using AlexWoW.Common.Network;
using AlexWoW.WorldServer.Protocol;

namespace AlexWoW.WorldServer.Net;

/// <summary>Принятый от клиента пакет: опкод + тело (без заголовка).</summary>
public readonly struct IncomingPacket(WorldOpcode opcode, byte[] body)
{
    public WorldOpcode Opcode { get; } = opcode;
    public byte[] Body { get; } = body;

    /// <summary>Новый ридер по телу пакета.</summary>
    public ByteReader Reader() => new(Body);
}
