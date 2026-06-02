namespace AlexWoW.AuthServer.Protocol;

/// <summary>Коды результата логина, отправляемые клиенту (совпадают с CMaNGOS/TrinityCore).</summary>
public enum AuthResult : byte
{
    Success = 0x00,
    FailBanned = 0x03,
    FailUnknownAccount = 0x04,
    FailIncorrectPassword = 0x05,
    FailAlreadyOnline = 0x06,
    FailNoTime = 0x07,
    FailDbBusy = 0x08,
    FailVersionInvalid = 0x09,
    FailVersionUpdate = 0x0A,
    FailSuspended = 0x0C,
    FailNoAccess = 0x0D,
}
