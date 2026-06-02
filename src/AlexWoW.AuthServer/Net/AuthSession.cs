using System.Net.Sockets;
using System.Security.Cryptography;
using AlexWoW.AuthServer.Protocol;
using AlexWoW.Common.Network;
using AlexWoW.Cryptography;
using AlexWoW.Database;
using AlexWoW.Database.Models;
using Microsoft.Extensions.Logging;

namespace AlexWoW.AuthServer.Net;

/// <summary>
/// Обслуживает одно TCP-соединение логин-протокола: challenge → proof → realm list.
/// </summary>
public sealed class AuthSession(
    Socket socket,
    AuthDatabase database,
    ILogger logger)
{
    private const ushort ExpectedBuild = 12340; // WotLK 3.3.5a

    private readonly NetworkStream _stream = new(socket, ownsSocket: true);
    private readonly string _remoteIp = (socket.RemoteEndPoint as System.Net.IPEndPoint)?.Address.ToString() ?? "?";

    private string? _username;
    private Account? _account;
    private Srp6Server? _srp;

    public async Task RunAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var commandByte = new byte[1];
                var read = await _stream.ReadAsync(commandByte, ct);
                if (read == 0)
                    break; // соединение закрыто

                var command = (AuthCommand)commandByte[0];
                switch (command)
                {
                    case AuthCommand.LogonChallenge:
                        await HandleLogonChallengeAsync(ct);
                        break;
                    case AuthCommand.LogonProof:
                        await HandleLogonProofAsync(ct);
                        break;
                    case AuthCommand.RealmList:
                        await HandleRealmListAsync(ct);
                        break;
                    default:
                        logger.LogWarning("Неизвестный опкод 0x{Command:X2} от {Ip}", commandByte[0], _remoteIp);
                        return;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or InvalidOperationException)
        {
            logger.LogDebug("Соединение {Ip} закрыто: {Message}", _remoteIp, ex.Message);
        }
        finally
        {
            await _stream.DisposeAsync();
        }
    }

    private async Task HandleLogonChallengeAsync(CancellationToken ct)
    {
        // Заголовок: error(1) + size(2)
        var header = new byte[3];
        await _stream.ReadExactlyAsync(header, ct);
        var size = (ushort)(header[1] | (header[2] << 8));

        var body = new byte[size];
        await _stream.ReadExactlyAsync(body, ct);

        var reader = new ByteReader(body);
        reader.Skip(4);                  // gamename "\0WoW"
        reader.Skip(3);                  // version 1/2/3
        var build = reader.UInt16();     // build
        reader.Skip(4);                  // platform
        reader.Skip(4);                  // os
        reader.Skip(4);                  // country
        reader.UInt32();                 // timezone bias
        reader.UInt32();                 // client ip
        var usernameLength = reader.UInt8();
        _username = reader.FixedString(usernameLength).ToUpperInvariant();

        logger.LogInformation("Challenge: '{User}' (build {Build}) от {Ip}", _username, build, _remoteIp);

        if (build != ExpectedBuild)
        {
            await SendChallengeErrorAsync(AuthResult.FailVersionInvalid, ct);
            return;
        }

        _account = await database.GetAccountByUsernameAsync(_username, ct);
        if (_account is null)
        {
            logger.LogInformation("Аккаунт '{User}' не найден", _username);
            await SendChallengeErrorAsync(AuthResult.FailUnknownAccount, ct);
            return;
        }

        _srp = new Srp6Server(_username, _account.Salt, _account.Verifier);

        var versionChallenge = new byte[16];
        RandomNumberGenerator.Fill(versionChallenge);

        var writer = new ByteWriter(120);
        writer.UInt8((byte)AuthCommand.LogonChallenge)
              .UInt8(0x00)                              // error placeholder
              .UInt8((byte)AuthResult.Success)
              .Bytes(_srp.B)                            // B (32)
              .UInt8(1).Bytes(Srp6.GBytes)             // g length + g
              .UInt8((byte)Srp6.KeyLength).Bytes(Srp6.NBytes) // N length + N
              .Bytes(_srp.Salt)                        // salt (32)
              .Bytes(versionChallenge)                 // CRC salt (16)
              .UInt8(0x00);                            // security flags

        await SendAsync(writer.ToArray(), ct);
    }

    private async Task HandleLogonProofAsync(CancellationToken ct)
    {
        // A(32) + M1(20) + crc(20) + numKeys(1) + secFlags(1) = 74
        var body = new byte[74];
        await _stream.ReadExactlyAsync(body, ct);

        if (_srp is null || _account is null)
        {
            logger.LogWarning("Proof без предшествующего challenge от {Ip}", _remoteIp);
            return;
        }

        var clientA = body.AsSpan(0, 32);
        var clientM1 = body.AsSpan(32, 20);

        if (!_srp.TryVerifyProof(clientA, clientM1, out var sessionKey, out var serverM2))
        {
            logger.LogInformation("Неверный пароль для '{User}'", _username);
            var fail = new ByteWriter(4)
                .UInt8((byte)AuthCommand.LogonProof)
                .UInt8((byte)AuthResult.FailIncorrectPassword)
                .UInt16(0);
            await SendAsync(fail.ToArray(), ct);
            return;
        }

        await database.SetSessionKeyAsync(_account.Id, sessionKey, _remoteIp, ct);
        logger.LogInformation("Успешный логин '{User}' от {Ip}", _username, _remoteIp);

        var writer = new ByteWriter(32)
            .UInt8((byte)AuthCommand.LogonProof)
            .UInt8((byte)AuthResult.Success)
            .Bytes(serverM2)        // M2 (20)
            .UInt32(0x00800000)     // account flags
            .UInt32(0)              // survey id
            .UInt16(0);             // unk flags
        await SendAsync(writer.ToArray(), ct);
    }

    private async Task HandleRealmListAsync(CancellationToken ct)
    {
        var unused = new byte[4];
        await _stream.ReadExactlyAsync(unused, ct);

        var realms = await database.GetRealmsAsync(ct);

        var inner = new ByteWriter(128);
        inner.UInt32(0)                          // unused
             .UInt16((ushort)realms.Count);
        foreach (var realm in realms)
        {
            inner.UInt8(realm.Type)
                 .UInt8(0x00)                    // lock
                 .UInt8(realm.Flags)
                 .CString(realm.Name)
                 .CString($"{realm.Address}:{realm.Port}")
                 .Single(realm.Population)
                 .UInt8(0x00)                    // число персонажей на реалме
                 .UInt8(realm.Timezone)
                 .UInt8((byte)realm.Id);
        }
        inner.UInt8(0x10).UInt8(0x00);           // трейлер

        var innerBytes = inner.ToArray();
        var packet = new ByteWriter(innerBytes.Length + 3)
            .UInt8((byte)AuthCommand.RealmList)
            .UInt16((ushort)innerBytes.Length)
            .Bytes(innerBytes);

        await SendAsync(packet.ToArray(), ct);
        logger.LogInformation("Отправлен список из {Count} реалмов на {Ip}", realms.Count, _remoteIp);
    }

    private async Task SendChallengeErrorAsync(AuthResult result, CancellationToken ct)
    {
        var writer = new ByteWriter(3)
            .UInt8((byte)AuthCommand.LogonChallenge)
            .UInt8(0x00)
            .UInt8((byte)result);
        await SendAsync(writer.ToArray(), ct);
    }

    private async Task SendAsync(byte[] data, CancellationToken ct)
    {
        await _stream.WriteAsync(data, ct);
        await _stream.FlushAsync(ct);
    }
}
