using System.Buffers.Binary;
using System.Security.Cryptography;
using AlexWoW.Common.Network;
using AlexWoW.Cryptography;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>Handshake world-сессии: вызов и проверка аутентификации.</summary>
public static class AuthHandlers
{
    /// <summary>Сервер инициирует handshake: SMSG_AUTH_CHALLENGE сразу при подключении.</summary>
    public static async Task SendAuthChallengeAsync(WorldSession session, CancellationToken ct)
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
        session.Logger.LogInformation("Отправлен SMSG_AUTH_CHALLENGE на {Ip}", session.RemoteIp);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgAuthSession)]
    public static async Task OnAuthSession(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var reader = packet.Reader();
        var build = reader.UInt32();
        reader.UInt32();                          // loginServerId
        var account = reader.CString().ToUpperInvariant();
        reader.UInt32();                          // loginServerType
        var clientSeed = reader.UInt32();
        reader.UInt32();                          // regionId
        reader.UInt32();                          // battlegroupId
        reader.UInt32();                          // realmId
        reader.Skip(8);                           // dosResponse (uint64)
        var clientDigest = reader.Bytes(20).ToArray();

        session.Account = account;
        session.Logger.LogInformation("CMSG_AUTH_SESSION: '{User}' (build {Build}) от {Ip}",
            account, build, session.RemoteIp);

        if (build != session.Options.ExpectedBuild)
        {
            session.Logger.LogWarning("Неподдерживаемый build {Build} (ожидается {Expected}) от {Ip}",
                build, session.Options.ExpectedBuild, session.RemoteIp);
            return;
        }

        var dbAccount = await session.Database.GetAccountByUsernameAsync(account, ct);
        if (dbAccount?.SessionKey is null)
        {
            session.Logger.LogWarning("Нет session key для '{User}' — клиент не проходил логин?", account);
            return;
        }

        var expected = WorldAuth.ComputeAuthSessionDigest(account, clientSeed, session.AuthSeed, dbAccount.SessionKey);
        if (!CryptographicOperations.FixedTimeEquals(expected, clientDigest))
        {
            session.Logger.LogWarning("Неверный auth digest для '{User}' от {Ip}", account, session.RemoteIp);
            return;
        }

        session.AccountId = dbAccount.Id;
        session.InitCrypt(dbAccount.SessionKey); // дальше заголовки шифруются

        var response = new ByteWriter(11)
            .UInt8((byte)AuthResponseCode.Ok)
            .UInt32(0)  // billing time remaining
            .UInt8(0)   // billing flags
            .UInt32(0)  // billing time rested
            .UInt8(2)   // expansion: 2 = WotLK
            .ToArray();
        await session.SendAsync(WorldOpcode.SmsgAuthResponse, response, ct);
        session.Logger.LogInformation("Успешный world-вход '{User}' от {Ip} (шифрование включено)",
            account, session.RemoteIp);
    }

    [WorldOpcodeHandler(WorldOpcode.CmsgPing)]
    public static async Task OnPing(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        var reader = packet.Reader();
        var ping = reader.UInt32();
        await session.SendAsync(WorldOpcode.SmsgPong, new ByteWriter(4).UInt32(ping).ToArray(), ct);
    }
}
