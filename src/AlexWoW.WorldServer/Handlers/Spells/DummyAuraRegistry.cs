using System.Collections.Frozen;
using System.Reflection;
using AlexWoW.WorldServer.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers.Spells;

/// <summary>
/// Реестр скриптовых аур (SPELL.T2): таблица <c>spellId → <see cref="IDummyAuraHandler"/></c>, собранная
/// сканом сборки по атрибуту <see cref="DummyAuraHandlerAttribute"/>. Добавить таль скрипт = добавить
/// класс с атрибутом и зависимости через DI — правка реестра не нужна.
/// Эталон — CMaNGOS spell scripts (<c>Spells/Scripts/ClassScripts/*</c>).
/// </summary>
internal sealed class DummyAuraRegistry
{
    private readonly FrozenDictionary<uint, IDummyAuraHandler> _handlers;

    public DummyAuraRegistry(IEnumerable<IDummyAuraHandler> handlers, ILogger<DummyAuraRegistry> logger)
    {
        var table = new Dictionary<uint, IDummyAuraHandler>();
        foreach (var handler in handlers)
        {
            foreach (var attr in handler.GetType().GetCustomAttributes<DummyAuraHandlerAttribute>())
            {
                foreach (var spellId in attr.SpellIds)
                {
                    if (!table.TryAdd(spellId, handler))
                    {
                        logger.LogWarning("DummyAuraHandler: двойная регистрация spell={Spell} ({Handler} проигнорирован)",
                            spellId, handler.GetType().Name);
                    }
                }
            }
        }
        _handlers = table.ToFrozenDictionary();
    }

    /// <summary>Число spell-id с зарегистрированным DUMMY-обработчиком (диагностика старта).</summary>
    public int HandlerCount => _handlers.Count;

    /// <summary>Зарегистрирован ли DUMMY-обработчик для этого спелла.</summary>
    public bool Has(uint spellId) => _handlers.ContainsKey(spellId);

    public Task OnApplyAsync(WorldSession session, uint spellId, CancellationToken ct)
        => _handlers.TryGetValue(spellId, out var h) ? h.OnApplyAsync(session, spellId, ct) : Task.CompletedTask;

    public Task OnRemoveAsync(WorldSession session, uint spellId, CancellationToken ct)
        => _handlers.TryGetValue(spellId, out var h) ? h.OnRemoveAsync(session, spellId, ct) : Task.CompletedTask;

    public Task<bool> OnProcAsync(WorldSession session, uint spellId, DummyProcContext ctx, CancellationToken ct)
        => _handlers.TryGetValue(spellId, out var h) ? h.OnProcAsync(session, spellId, ctx, ct) : Task.FromResult(false);
}

/// <summary>DI-расширение: скан сборки на <see cref="IDummyAuraHandler"/>-классы (как DI-singletons +
/// форвард в <see cref="DummyAuraRegistry"/>). Зеркало <see cref="HandlerRegistration.AddWorldOpcodeHandlers"/>.</summary>
internal static class DummyAuraRegistration
{
    public static IServiceCollection AddDummyAuraHandlers(this IServiceCollection services)
    {
        var handlerTypes = typeof(DummyAuraRegistration).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false }
                        && typeof(IDummyAuraHandler).IsAssignableFrom(t));
        foreach (var type in handlerTypes)
        {
            services.AddSingleton(type);
            services.AddSingleton(typeof(IDummyAuraHandler), sp => (IDummyAuraHandler)sp.GetRequiredService(type));
        }
        services.AddSingleton<DummyAuraRegistry>();
        return services;
    }
}
