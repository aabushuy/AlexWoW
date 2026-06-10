using Microsoft.Extensions.DependencyInjection;

namespace AlexWoW.WorldServer.Handlers.Dev;

/// <summary>
/// Регистрация dev-команд в DI (M7 S8): скан сборки на <see cref="IDevCommand"/> — добавить команду =
/// добавить класс, правка Program.cs не нужна (тот же паттерн, что AddWorldOpcodeHandlers для опкодов).
/// </summary>
internal static class DevCommandRegistration
{
    public static IServiceCollection AddDevCommands(this IServiceCollection services)
    {
        var commandTypes = typeof(DevCommandRegistration).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false }
                        && typeof(IDevCommand).IsAssignableFrom(t));
        foreach (var type in commandTypes)
        {
            // Конкретный тип + форвард на маркер: один экземпляр и как зависимость, и в списке реестра.
            services.AddSingleton(type);
            services.AddSingleton(typeof(IDevCommand), sp => sp.GetRequiredService(type));
        }

        services.AddSingleton<DevCommandRegistry>();
        services.AddSingleton<DevCommandDispatcher>();
        return services;
    }
}
