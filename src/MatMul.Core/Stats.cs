namespace MatMul.Core;

public static class Stats
{
    /// <summary>
    /// Методика из задания: отбросить самое большое и самое маленькое значение,
    /// остальные усреднить. Если замеров меньше 3 — обычное среднее.
    /// </summary>
    public static double TrimmedMean(IReadOnlyList<double> values)
    {
        if (values.Count == 0) throw new ArgumentException("Нет значений", nameof(values));

        var sorted = new List<double>(values);
        sorted.Sort();

        int from = 0;
        int to = sorted.Count;
        if (sorted.Count >= 3)
        {
            from = 1;
            to = sorted.Count - 1;
        }

        double sum = 0;
        for (int i = from; i < to; i++) sum += sorted[i];
        return sum / (to - from);
    }
}
