using Microsoft.Extensions.DependencyInjection;

namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Регистрация опкод-модулей в DI (M7 #35): скан сборки на <see cref="IOpcodeHandlerModule"/> —
/// добавить модуль = добавить класс, правка Program.cs не нужна.
/// </summary>
internal static class HandlerRegistration
{
    public static IServiceCollection AddWorldOpcodeHandlers(this IServiceCollection services)
    {
        var moduleTypes = typeof(HandlerRegistration).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false }
                        && typeof(IOpcodeHandlerModule).IsAssignableFrom(t));
        foreach (var type in moduleTypes)
        {
            // Конкретный тип + форвард на маркер: модуль резолвится и как зависимость (инъекция
            // модуля/сервиса в модуль), и в общий список для роутера — один экземпляр.
            services.AddSingleton(type);
            services.AddSingleton(typeof(IOpcodeHandlerModule), sp => sp.GetRequiredService(type));
        }

        services.AddSingleton<WorldPacketRouter>();
        return services;
    }
}
