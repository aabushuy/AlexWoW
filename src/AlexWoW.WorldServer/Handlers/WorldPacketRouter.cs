using System.Collections.Frozen;
using System.Reflection;
using AlexWoW.WorldServer.Net;
using AlexWoW.WorldServer.Protocol;
using Microsoft.Extensions.Logging;

namespace AlexWoW.WorldServer.Handlers;

internal delegate Task WorldOpcodeHandler(WorldSession session, IncomingPacket packet, CancellationToken ct);

/// <summary>
/// Реестр обработчиков опкодов (M7 #35: DI-синглтон). Таблица <c>opcode → handler</c> строится один раз
/// из методов DI-модулей <see cref="IOpcodeHandlerModule"/>, помеченных <see cref="WorldOpcodeHandlerAttribute"/>.
/// Добавить опкод = добавить метод с атрибутом; центральный switch не нужен.
/// Миграция M7 завершена (S7): статического фолбэка больше нет — все хендлеры живут в DI-модулях,
/// остаточные статики с атрибутом валят старт (см. страховку в конструкторе).
/// </summary>
internal sealed class WorldPacketRouter
{
    private readonly FrozenDictionary<WorldOpcode, WorldOpcodeHandler> _handlers;

    public WorldPacketRouter(IEnumerable<IOpcodeHandlerModule> modules, ILogger<WorldPacketRouter> logger)
    {
        var table = new Dictionary<WorldOpcode, WorldOpcodeHandler>();

        foreach (var module in modules)
        {
            foreach (var method in module.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                foreach (var attribute in method.GetCustomAttributes<WorldOpcodeHandlerAttribute>())
                {
                    var handler = (WorldOpcodeHandler)Delegate.CreateDelegate(
                        typeof(WorldOpcodeHandler), module, method);
                    foreach (var opcode in attribute.Opcodes)
                    {
                        if (!table.TryAdd(opcode, handler))
                            logger.LogWarning("Опкод {Opcode}: двойная регистрация в модулях ({Module}.{Method} проигнорирован)",
                                opcode, module.GetType().Name, method.Name);
                    }
                }
            }
        }

        // Страховка от потерянных опкодов (M7 S7): статический [WorldOpcodeHandler]-метод вне DI-модуля
        // в таблицу не попал бы молча — лучше уронить старт с именами виновников, чем терять опкоды.
        var orphans = typeof(WorldPacketRouter).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            .Where(m => m.GetCustomAttributes<WorldOpcodeHandlerAttribute>().Any())
            .Select(m => $"{m.DeclaringType?.Name}.{m.Name}")
            .ToArray();
        if (orphans.Length > 0)
            throw new InvalidOperationException(
                $"Статические методы с [WorldOpcodeHandler] вне DI-модулей: {string.Join(", ", orphans)} — " +
                "сконвертируйте их в IOpcodeHandlerModule (миграция M7 завершена, фолбэка нет)");

        _handlers = table.ToFrozenDictionary();
    }

    /// <summary>Число зарегистрированных опкодов (для диагностики при старте).</summary>
    public int HandlerCount => _handlers.Count;

    public async Task DispatchAsync(WorldSession session, IncomingPacket packet, CancellationToken ct)
    {
        if (_handlers.TryGetValue(packet.Opcode, out var handler))
            await handler(session, packet, ct);
        else
            session.Logger.LogInformation("Опкод {Opcode} (0x{Value:X}) от {Ip} — без обработчика",
                packet.Opcode, (uint)packet.Opcode, session.RemoteIp);
    }
}
