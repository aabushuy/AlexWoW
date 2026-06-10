using AlexWoW.WorldServer.Handlers.Dev;
using AlexWoW.WorldServer.Net;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Диспетчер dev/тест-команд через чат: сообщение, начинающееся с '.', не уходит в чат, а выполняет команду
/// (прокачка/выдача/расстановка dev-сущностей). Сами команды — отдельные SRP-классы <see cref="IDevCommand"/>
/// в <c>Handlers/Dev/</c>, авто-регистрируемые в <see cref="DevCommandRegistry"/> (Command-паттерн; добавить
/// команду = добавить класс, диспетчер не трогаем). Гейт — флаг администратора (<c>account.is_admin = 1</c>).
/// </summary>
public static class DevCommands
{
    /// <summary>Выполнить, если это dev-команда. true → обработано (в чат не слать).</summary>
    public static async Task<bool> TryHandleAsync(WorldSession session, string text, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(text) || text[0] != '.')
            return false;
        if (!session.IsAdmin)
        {
            await DevChat.ReplyAsync(session, "Команда доступна только администраторам.", ct);
            return true; // в чат не уходит (это была попытка команды)
        }

        var parts = text[1..].Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return false;
        var name = parts[0].ToLowerInvariant();

        var command = DevCommandRegistry.Find(name);
        if (command is null)
        {
            await DevChat.ReplyAsync(session, $"Неизвестная команда: .{name} (.help)", ct);
            return true;
        }

        try
        {
            if (command.RequiresWorld && session.InWorldGuid == 0)
            {
                await DevChat.ReplyAsync(session, "Доступно только в мире", ct);
            }
            else
            {
                var ctx = new DevCommandContext(session, parts.Skip(1).ToArray());
                await command.ExecuteAsync(ctx, ct);
            }
        }
        catch (Exception ex)
        {
            session.Logger.LogWarning("DEV команда '.{Cmd}' ошибка: {Msg}", name, ex.Message);
            await DevChat.ReplyAsync(session, $"Ошибка: {ex.Message}", ct);
        }
        return true;
    }
}
