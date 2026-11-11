namespace MatMul.Core;

/// <summary>
/// Общий счётчик выполненной работы (в строках). К нему одновременно обращаются
/// все рабочие потоки, поэтому увеличение сделано атомарно (Interlocked).
/// Без этого "done++" (прочитать - прибавить - записать) терял бы обновления:
/// два потока прочитали бы одно и то же значение и записали бы одно и то же +1.
/// </summary>
public sealed class ProgressTracker
{
    private long _done;

    public long Total { get; }

    public ProgressTracker(long total)
    {
        Total = total;
    }

    public void Add(long count) => Interlocked.Add(ref _done, count);

    public long Done => Interlocked.Read(ref _done);

    public int Percent => Total <= 0 ? 100 : (int)(Done * 100 / Total);
}

public static class ProgressReporter
{
    /// <summary>
    /// Главный поток ждёт рабочие потоки. Если output != null, пока они считают,
    /// он печатает прогресс — но только когда изменился целый процент и не чаще,
    /// чем раз в pollMs миллисекунд (вывод в консоль медленный).
    /// После завершения всех потоков всегда печатается итоговое значение.
    /// </summary>
    public static void WaitWithProgress(IReadOnlyList<Thread> workers, ProgressTracker tracker,
                                        TextWriter? output, int pollMs = 100)
    {
        if (output == null)
        {
            foreach (Thread w in workers) w.Join();
            return;
        }

        int last = -1;
        while (true)
        {
            bool anyAlive = false;
            foreach (Thread w in workers)
            {
                if (w.IsAlive)
                {
                    anyAlive = true;
                    break;
                }
            }

            int percent = tracker.Percent;
            if (percent != last)
            {
                output.Write($"\rПрогресс: {percent}%");
                output.Flush();
                last = percent;
            }

            if (!anyAlive) break;
            Thread.Sleep(pollMs);
        }

        foreach (Thread w in workers) w.Join();
        output.WriteLine($"\rПрогресс: {tracker.Percent}%");
    }
}
