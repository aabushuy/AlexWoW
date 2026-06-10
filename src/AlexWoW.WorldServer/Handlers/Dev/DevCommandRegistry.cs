using System.Reflection;

namespace AlexWoW.WorldServer.Handlers.Dev;

/// <summary>
/// Реестр dev-команд: таблица <c>имя/алиас → команда</c>, построенная один раз рефлексией из всех
/// реализаций <see cref="IDevCommand"/> с публичным конструктором без параметров. Аналог
/// <c>WorldPacketRouter</c> для опкодов — добавить команду = добавить класс, реестр не трогаем.
/// </summary>
internal static class DevCommandRegistry
{
    private static readonly Dictionary<string, IDevCommand> ByName = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Команды в порядке <see cref="IDevCommand.Order"/> (для <c>.help</c>).</summary>
    public static IReadOnlyList<IDevCommand> Ordered { get; }

    static DevCommandRegistry()
    {
        var commands = typeof(DevCommandRegistry).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false }
                        && typeof(IDevCommand).IsAssignableFrom(t)
                        && t.GetConstructor(Type.EmptyTypes) is not null)
            .Select(t => (IDevCommand)Activator.CreateInstance(t)!)
            .OrderBy(c => c.Order)
            .ToList();

        foreach (var c in commands)
            foreach (var name in c.Names)
                ByName[name] = c;

        Ordered = commands;
    }

    /// <summary>Команда по имени/алиасу (без точки), либо null.</summary>
    public static IDevCommand? Find(string name) => ByName.TryGetValue(name, out var c) ? c : null;
}
