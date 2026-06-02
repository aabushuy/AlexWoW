namespace AlexWoW.Cryptography;

/// <summary>
/// Потоковый шифр RC4 (ARC4). В .NET BCL отсутствует, поэтому реализован вручную.
/// Используется для шифрования заголовков world-пакетов в WotLK 3.3.5a.
/// Экземпляр хранит состояние потока — один на направление (in/out).
/// </summary>
public sealed class Arc4
{
    private readonly byte[] _state = new byte[256];
    private int _x;
    private int _y;

    public Arc4(ReadOnlySpan<byte> key)
    {
        for (var i = 0; i < 256; i++)
            _state[i] = (byte)i;

        int j = 0;
        for (var i = 0; i < 256; i++)
        {
            j = (j + _state[i] + key[i % key.Length]) & 0xFF;
            (_state[i], _state[j]) = (_state[j], _state[i]);
        }
    }

    /// <summary>Обрабатывает буфер на месте (RC4 симметричен: шифрование = дешифрование).</summary>
    public void Process(Span<byte> data)
    {
        for (var i = 0; i < data.Length; i++)
        {
            _x = (_x + 1) & 0xFF;
            _y = (_y + _state[_x]) & 0xFF;
            (_state[_x], _state[_y]) = (_state[_y], _state[_x]);
            var k = _state[(_state[_x] + _state[_y]) & 0xFF];
            data[i] ^= k;
        }
    }

    /// <summary>«Прогрев»: пропускает <paramref name="count"/> нулевых байт, отбрасывая слабое начало потока.</summary>
    public void Drop(int count)
    {
        Span<byte> garbage = count <= 1024 ? stackalloc byte[count] : new byte[count];
        garbage.Clear();
        Process(garbage);
    }
}
