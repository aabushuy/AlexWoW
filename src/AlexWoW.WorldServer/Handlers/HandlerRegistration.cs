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
            services.AddSingleton(typeof(IOpcodeHandlerModule), type);

        services.AddSingleton<WorldPacketRouter>();
        return services;
    }
}
