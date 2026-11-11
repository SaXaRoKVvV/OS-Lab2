namespace MatMul.Core;

/// <summary>
/// Порядок вложенных циклов.
/// IJK — "школьный": во внутреннем цикле B читается ПО СТОЛБЦУ (много промахов кэша).
/// IKJ — внутренний цикл идёт подряд по строке B и строке C (кэш-дружелюбный).
/// Число операций одинаковое.
/// </summary>
public enum LoopOrder
{
    IJK,
    IKJ
}

public static class DenseMultiplier
{
    /// <summary>
    /// C = A * B с помощью threads потоков, созданных вручную.
    /// Строки C делятся между потоками статически, поровну (см. Partition).
    /// Каждый поток пишет только в СВОИ строки общей матрицы C — поэтому
    /// блокировка для C не нужна (никто другой в эти ячейки не пишет).
    /// После каждой строки поток увеличивает общий счётчик прогресса.
    /// Время работы снаружи измеряет вызывающий код (создание потоков входит в замер).
    /// </summary>
    public static Matrix Multiply(Matrix a, Matrix b, int threads,
                                  LoopOrder order = LoopOrder.IKJ,
                                  ProgressTracker? progress = null,
                                  TextWriter? progressOutput = null)
    {
        if (a.Cols != b.Rows)
            throw new ArgumentException(
                $"Несовместимые размеры: A {a.Rows}x{a.Cols}, B {b.Rows}x{b.Cols} (нужно A.Cols == B.Rows)");
        if (threads < 1)
            throw new ArgumentOutOfRangeException(nameof(threads), "Число потоков должно быть >= 1");

        var c = new Matrix(a.Rows, b.Cols);
        ProgressTracker tracker = progress ?? new ProgressTracker(a.Rows);
        var ranges = Partition.StaticRanges(a.Rows, threads);

        var workers = new Thread[threads];
        for (int t = 0; t < threads; t++)
        {
            int start = ranges[t].Start;
            int end = ranges[t].End;
            workers[t] = new Thread(() => MultiplyRows(a, b, c, start, end, order, tracker));
        }

        foreach (Thread w in workers) w.Start();
        ProgressReporter.WaitWithProgress(workers, tracker, progressOutput);
        return c;
    }

    private static void MultiplyRows(Matrix a, Matrix b, Matrix c, int rowStart, int rowEnd,
                                     LoopOrder order, ProgressTracker progress)
    {
        int m = a.Cols;
        int k = b.Cols;
        long[] A = a.Data;
        long[] B = b.Data;
        long[] C = c.Data;

        for (int i = rowStart; i < rowEnd; i++)
        {
            int aRow = i * m;
            int cRow = i * k;

            if (order == LoopOrder.IJK)
            {
                for (int j = 0; j < k; j++)
                {
                    long sum = 0;
                    for (int t = 0; t < m; t++)
                        sum += A[aRow + t] * B[t * k + j];   // B[t*k + j]: шаг k элементов — по столбцу
                    C[cRow + j] = sum;
                }
            }
            else
            {
                for (int t = 0; t < m; t++)
                {
                    long av = A[aRow + t];
                    int bRow = t * k;
                    for (int j = 0; j < k; j++)
                        C[cRow + j] += av * B[bRow + j];     // подряд по памяти
                }
            }

            progress.Add(1);
        }
    }
}
