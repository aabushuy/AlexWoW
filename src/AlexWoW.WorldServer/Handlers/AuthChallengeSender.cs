using System.Buffers.Binary;
using System.Security.Cryptography;
using AlexWoW.Common.Network;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Сервер инициирует handshake: SMSG_AUTH_CHALLENGE сразу при подключении (M2; в DI — M7 #35).
/// Вынесено из AuthHandlers: вызывается самой сессией до диспетчеризации, а не по опкоду.
/// </summary>
internal sealed class AuthChallengeSender(ILogger<AuthChallengeSender> logger)
{
    public async Task SendAsync(WorldSession session, CancellationToken ct)
    {
        Span<byte> seedBytes = stackalloc byte[4];
        RandomNumberGenerator.Fill(seedBytes);
        session.AuthSeed = BinaryPrimitives.ReadUInt32LittleEndian(seedBytes);

        var payload = new ByteWriter(40)
            .UInt32(1)
            .UInt32(session.AuthSeed)
            .Bytes(RandomNumberGenerator.GetBytes(32)) // seed1/seed2 — нашей схемой не используются
            .ToArray();
        await session.SendAsync(WorldOpcode.SmsgAuthChallenge, payload, ct);
        logger.LogInformation("Отправлен SMSG_AUTH_CHALLENGE на {Ip}", session.RemoteIp);
    }
}
