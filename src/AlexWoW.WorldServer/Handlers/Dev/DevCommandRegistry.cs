namespace AlexWoW.WorldServer.Handlers.Dev;

/// <summary>
/// Реестр dev-команд: таблица <c>имя/алиас → команда</c>, построенная один раз в конструкторе из всех
/// DI-зарегистрированных <see cref="IDevCommand"/> (скан сборки — <see cref="DevCommandRegistration"/>).
/// Аналог <c>WorldPacketRouter</c> для опкодов — добавить команду = добавить класс, реестр не трогаем.
/// DI-синглтон (M7 S8, бывший статик с Activator.CreateInstance).
/// </summary>
internal sealed class DevCommandRegistry
{
    private readonly Dictionary<string, IDevCommand> _byName = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Команды в порядке <see cref="IDevCommand.Order"/> (для <c>.help</c>).</summary>
    public IReadOnlyList<IDevCommand> Ordered { get; }

    public DevCommandRegistry(IEnumerable<IDevCommand> commands)
    {
        Ordered = [.. commands.OrderBy(c => c.Order)];
        foreach (var c in Ordered)
        {
            foreach (var name in c.Names)
                _byName[name] = c;
        }
    }

    /// <summary>Команда по имени/алиасу (без точки), либо null.</summary>
    public IDevCommand? Find(string name) => _byName.TryGetValue(name, out var c) ? c : null;
}
