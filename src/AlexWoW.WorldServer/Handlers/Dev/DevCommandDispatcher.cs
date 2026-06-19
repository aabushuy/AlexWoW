using AlexWoW.WorldServer.Net;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers.Dev;

/// <summary>
/// Диспетчер dev/тест-команд через чат: сообщение, начинающееся с '.', не уходит в чат, а выполняет команду
/// (прокачка/выдача/расстановка dev-сущностей). Сами команды — отдельные SRP-классы <see cref="IDevCommand"/>,
/// собранные DI-сканом в <see cref="DevCommandRegistry"/> (Command-паттерн; добавить команду = добавить класс,
/// диспетчер не трогаем). Гейт — флаг администратора (<c>account.is_admin = 1</c>).
/// DI-сервис (M7 S8, бывший статик <c>Handlers.DevCommands</c>), инжектится в <c>ChatHandlers</c>.
/// </summary>
internal sealed class DevCommandDispatcher(DevCommandRegistry registry, ChatNotifier chat)
{
    /// <summary>Выполнить, если это dev-команда. true → обработано (в чат не слать).</summary>
    public async Task<bool> TryHandleAsync(WorldSession session, string text, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(text) || text[0] != '.')
            return false;
        if (!session.IsAdmin)
        {
            await chat.SendSystemAsync(session, "Команда доступна только администраторам.", ct);
            return true; // в чат не уходит (это была попытка команды)
        }

        var parts = text[1..].Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return false;
        var name = parts[0].ToLowerInvariant();

        var command = registry.Find(name);
        if (command is null)
        {
            await chat.SendSystemAsync(session, $"Неизвестная команда: .{name} (.help)", ct);
            return true;
        }

        try
        {
            if (command.RequiresWorld && session.InWorldGuid == 0)
            {
                await chat.SendSystemAsync(session, "Доступно только в мире", ct);
            }
            else
            {
                var ctx = new DevCommandContext(session, [.. parts.Skip(1)], chat, registry.Ordered);
                await command.ExecuteAsync(ctx, ct);
            }
        }
        catch (Exception ex)
        {
            session.Logger.LogWarning(ex, "DEV команда '.{Cmd}' ошибка: {Msg}", name, ex.Message);
            await chat.SendSystemAsync(session, $"Ошибка: {ex.Message}", ct);
        }
        return true;
    }
}
