namespace MatMul.Core;

public enum Distribution
{
    /// <summary>Строки заранее делятся на T непрерывных блоков с одинаковым числом строк.</summary>
    Static,
    /// <summary>Каждый поток берёт следующую порцию строк из общего счётчика, пока строки не кончатся.</summary>
    Dynamic
}

public static class TriangularMultiplier
{
    /// <summary>
    /// C = A * B, где A — нижнетреугольная N x N, B — обычная N x K.
    /// C[i][j] = A[i][0]*B[0][j] + ... + A[i][i]*B[i][j]  (у строки i всего i+1 слагаемых).
    /// Работа на строку растёт с номером строки: последняя строка в N раз тяжелее первой.
    /// </summary>
    /// <param name="chunk">Размер порции строк для динамического способа.</param>
    /// <param name="rowHits">
    /// Необязательный массив для тестов: rowHits[i] увеличивается каждый раз,
    /// когда строка i посчитана. После работы там должны быть одни единицы.
    /// </param>
    public static Matrix Multiply(TriangularMatrix a, Matrix b, int threads, Distribution distribution,
                                  int chunk = 1,
                                  ProgressTracker? progress = null,
                                  TextWriter? progressOutput = null,
                                  int[]? rowHits = null)
    {
        if (a.N != b.Rows)
            throw new ArgumentException(
                $"Несовместимые размеры: A {a.N}x{a.N}, B {b.Rows}x{b.Cols}");
        if (threads < 1)
            throw new ArgumentOutOfRangeException(nameof(threads), "Число потоков должно быть >= 1");
        if (chunk < 1)
            throw new ArgumentOutOfRangeException(nameof(chunk), "Размер порции должен быть >= 1");
        if (rowHits != null && rowHits.Length != a.N)
            throw new ArgumentException("rowHits должен иметь длину N");

        int n = a.N;
        var c = new Matrix(n, b.Cols);
        ProgressTracker tracker = progress ?? new ProgressTracker(n);
        var workers = new Thread[threads];

        if (distribution == Distribution.Static)
        {
            var ranges = Partition.StaticRanges(n, threads);
            for (int t = 0; t < threads; t++)
            {
                int start = ranges[t].Start;
                int end = ranges[t].End;
                workers[t] = new Thread(() =>
                {
                    for (int i = start; i < end; i++)
                        ComputeRow(a, b, c, i, tracker, rowHits);
                });
            }
        }
        else
        {
            var dispenser = new RowDispenser(n, chunk);
            for (int t = 0; t < threads; t++)
            {
                workers[t] = new Thread(() =>
                {
                    while (dispenser.TryTake(out int start, out int end))
                    {
                        for (int i = start; i < end; i++)
                            ComputeRow(a, b, c, i, tracker, rowHits);
                    }
                });
            }
        }

        foreach (Thread w in workers) w.Start();
        ProgressReporter.WaitWithProgress(workers, tracker, progressOutput);
        return c;
    }

    private static void ComputeRow(TriangularMatrix a, Matrix b, Matrix c, int i,
                                   ProgressTracker progress, int[]? rowHits)
    {
        int k = b.Cols;
        long[] aRow = a.Rows[i];     // длина i + 1
        long[] B = b.Data;
        long[] C = c.Data;
        int cRow = i * k;

        for (int t = 0; t <= i; t++)
        {
            long av = aRow[t];
            int bRow = t * k;
            for (int j = 0; j < k; j++)
                C[cRow + j] += av * B[bRow + j];
        }

        if (rowHits != null) Interlocked.Increment(ref rowHits[i]);
        progress.Add(1);
    }
}
