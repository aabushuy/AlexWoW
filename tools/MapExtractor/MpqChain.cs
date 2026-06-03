using Foole.Mpq;

namespace AlexWoW.MapExtractor;

/// <summary>
/// Цепочка MPQ клиента 3.3.5a с приоритетом патчей: файл берётся из самого «верхнего»
/// архива, где он есть (patch-3 &gt; … &gt; common). Чтение по точному имени (хеш, listfile не нужен).
/// </summary>
public sealed class MpqChain : IDisposable
{
    // Приоритет: чем меньше индекс — тем выше (ищем сверху вниз). Локаль-патчи и патчи поверх базы.
    private static readonly string[] Priority =
    {
        "patch-ruRU-3.MPQ", "patch-ruRU-2.MPQ", "patch-ruRU.MPQ",
        "patch-3.MPQ", "patch-2.MPQ", "patch.MPQ",
        "lichking-locale-ruRU.MPQ", "expansion-locale-ruRU.MPQ", "locale-ruRU.MPQ", "base-ruRU.MPQ",
        "lichking.MPQ", "expansion.MPQ", "common-2.MPQ", "common.MPQ",
    };

    private readonly List<MpqArchive> _archives = new();

    public MpqChain(string dataDir)
    {
        var found = Directory.GetFiles(dataDir, "*.MPQ", SearchOption.AllDirectories);
        int Rank(string path)
        {
            var name = Path.GetFileName(path);
            var idx = Array.FindIndex(Priority, p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase));
            return idx < 0 ? int.MaxValue : idx;
        }
        foreach (var path in found.Where(p => Rank(p) != int.MaxValue).OrderBy(Rank))
            _archives.Add(new MpqArchive(path));
    }

    /// <summary>Читает файл из самого приоритетного архива, где он есть; null — если нигде нет.</summary>
    public byte[]? ReadFile(string name)
    {
        foreach (var archive in _archives)
        {
            if (!archive.FileExists(name))
                continue;
            using var s = archive.OpenFile(name);
            var buf = new byte[s.Length];
            var read = 0;
            while (read < buf.Length)
            {
                var n = s.Read(buf, read, buf.Length - read);
                if (n <= 0) break;
                read += n;
            }
            return buf;
        }
        return null;
    }

    public void Dispose()
    {
        foreach (var a in _archives)
            a.Dispose();
        _archives.Clear();
    }
}
