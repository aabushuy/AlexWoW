using System.Collections.Frozen;
using System.Reflection;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers.Spells;

/// <summary>
/// Реестр SPELL_EFFECT_DUMMY-обработчиков (зеркало <see cref="DummyAuraRegistry"/>): таблица
/// <c>spellId → <see cref="IEffectDummyHandler"/></c>, скан по атрибуту
/// <see cref="EffectDummyHandlerAttribute"/>. Добавить «голый» dummy-эффект (Slam/Execute/Mortal Strike) =
/// добавить класс, правка реестра не нужна.
/// </summary>
internal sealed class EffectDummyRegistry
{
    private readonly FrozenDictionary<uint, IEffectDummyHandler> _handlers;

    public EffectDummyRegistry(IEnumerable<IEffectDummyHandler> handlers, ILogger<EffectDummyRegistry> logger)
    {
        var table = new Dictionary<uint, IEffectDummyHandler>();
        foreach (var handler in handlers)
        {
            foreach (var attr in handler.GetType().GetCustomAttributes<EffectDummyHandlerAttribute>())
            {
                foreach (var spellId in attr.SpellIds)
                {
                    if (!table.TryAdd(spellId, handler))
                        logger.LogWarning("EffectDummyHandler: двойная регистрация spell={Spell} ({Handler} проигнорирован)",
                            spellId, handler.GetType().Name);
                }
            }
        }
        _handlers = table.ToFrozenDictionary();
    }

    public int HandlerCount => _handlers.Count;

    public bool Has(uint spellId) => _handlers.ContainsKey(spellId);

    public Task<bool> ApplyAsync(WorldSession session, uint spellId, SpellCatalog.SpellInfo info,
        ulong targetGuid, long now, CancellationToken ct)
        => _handlers.TryGetValue(spellId, out var h)
            ? h.ApplyAsync(session, spellId, info, targetGuid, now, ct)
            : Task.FromResult(false);
}

/// <summary>DI-расширение: скан сборки на <see cref="IEffectDummyHandler"/> + регистрация
/// <see cref="EffectDummyRegistry"/>.</summary>
internal static class EffectDummyRegistration
{
    public static IServiceCollection AddEffectDummyHandlers(this IServiceCollection services)
    {
        var handlerTypes = typeof(EffectDummyRegistration).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false }
                        && typeof(IEffectDummyHandler).IsAssignableFrom(t));
        foreach (var type in handlerTypes)
        {
            services.AddSingleton(type);
            services.AddSingleton(typeof(IEffectDummyHandler), sp => (IEffectDummyHandler)sp.GetRequiredService(type));
        }
        services.AddSingleton<EffectDummyRegistry>();
        return services;
    }
}
