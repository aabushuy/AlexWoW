namespace AlexWoW.WorldServer.Handlers;

/// <summary>
/// Маркер модуля опкод-хендлеров (M7 #35): sealed-класс с публичными методами, помеченными
/// <see cref="WorldOpcodeHandlerAttribute"/>. Модули регистрируются в DI assembly-сканом
/// (<see cref="HandlerRegistration.AddWorldOpcodeHandlers"/>), зависимости получают через конструктор;
/// <see cref="WorldPacketRouter"/> собирает их методы в таблицу диспетчеризации при старте.
/// Модуль обязан быть stateless: всё состояние — в WorldSession/WorldState.
/// </summary>
internal interface IOpcodeHandlerModule;
