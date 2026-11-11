namespace MatMul.Core;

/// <summary>Статическое разбиение строк на непрерывные блоки.</summary>
public static class Partition
{
    /// <summary>
    /// Делит rows строк между threads потоками поровну. Остаток (rows % threads)
    /// раздаётся по одной лишней строке первым потокам, поэтому блоки
    /// различаются не больше чем на 1 строку. Если потоков больше, чем строк,
    /// лишние потоки получают пустой диапазон [x, x).
    /// </summary>
    public static (int Start, int End)[] StaticRanges(int rows, int threads)
    {
        if (threads < 1) throw new ArgumentOutOfRangeException(nameof(threads), "Число потоков должно быть >= 1");
        if (rows < 0) throw new ArgumentOutOfRangeException(nameof(rows));

        var ranges = new (int Start, int End)[threads];
        int baseCount = rows / threads;
        int remainder = rows % threads;
        int start = 0;
        for (int t = 0; t < threads; t++)
        {
            int count = baseCount + (t < remainder ? 1 : 0);
            ranges[t] = (start, start + count);
            start += count;
        }
        return ranges;
    }
}
