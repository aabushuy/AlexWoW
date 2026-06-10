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
/// <para>Мост миграции: пока конверсия модулей не завершена, вторым проходом сканируются легаси-статические
/// методы; метод модуля приоритетнее. Фолбэк удаляется в финальном срезе (S7).</para>
/// </summary>
internal sealed class WorldPacketRouter
{
    private readonly FrozenDictionary<WorldOpcode, WorldOpcodeHandler> _handlers;

    public WorldPacketRouter(IEnumerable<IOpcodeHandlerModule> modules, ILogger<WorldPacketRouter> logger)
    {
        var table = new Dictionary<WorldOpcode, WorldOpcodeHandler>();

        // 1) Целевая схема: instance-методы DI-модулей.
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

        // 2) Временный фолбэк (мост M7): легаси-статические методы. Модуль приоритетнее.
        var staticMethods = typeof(WorldPacketRouter).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static));
        foreach (var method in staticMethods)
        {
            foreach (var attribute in method.GetCustomAttributes<WorldOpcodeHandlerAttribute>())
            {
                var handler = (WorldOpcodeHandler)Delegate.CreateDelegate(typeof(WorldOpcodeHandler), method);
                foreach (var opcode in attribute.Opcodes)
                {
                    if (!table.TryAdd(opcode, handler))
                        logger.LogDebug("Опкод {Opcode}: статик {Type}.{Method} перекрыт модулем",
                            opcode, method.DeclaringType?.Name, method.Name);
                }
            }
        }

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
