namespace MatMul.Core;

/// <summary>
/// Общий счётчик для ДИНАМИЧЕСКОГО распределения: каждый поток берёт следующую
/// порцию строк. Взятие порции — одна атомарная операция Interlocked.Add
/// (чтение + увеличение без возможности вклиниться другому потоку).
/// </summary>
public sealed class RowDispenser
{
    private int _next;
    private readonly int _total;
    private readonly int _chunk;

    public RowDispenser(int total, int chunk)
    {
        if (total < 0) throw new ArgumentOutOfRangeException(nameof(total));
        if (chunk < 1) throw new ArgumentOutOfRangeException(nameof(chunk), "Размер порции должен быть >= 1");
        _total = total;
        _chunk = chunk;
    }

    /// <summary>Взять следующую порцию строк [start, end). false — строки закончились.</summary>
    public bool TryTake(out int start, out int end)
    {
        int s = Interlocked.Add(ref _next, _chunk) - _chunk;
        if (s >= _total)
        {
            start = 0;
            end = 0;
            return false;
        }
        start = s;
        end = Math.Min(s + _chunk, _total);
        return true;
    }
}
