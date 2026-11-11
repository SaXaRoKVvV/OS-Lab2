using MatMul.Core;

namespace MatMul.Tests;

public class MultiplyTests
{
    // Заранее известный ответ, посчитанный вручную. Ловит принципиальные ошибки в формуле/индексах.
    [Fact]
    public void Known_result_2x2()
    {
        var a = Matrix.FromArray(new long[,] { { 1, 2 }, { 3, 4 } });
        var b = Matrix.FromArray(new long[,] { { 5, 6 }, { 7, 8 } });
        var expected = Matrix.FromArray(new long[,] { { 19, 22 }, { 43, 50 } });

        Reference.AssertSame(expected, DenseMultiplier.Multiply(a, b, 1));
        Reference.AssertSame(expected, DenseMultiplier.Multiply(a, b, 2));
    }

    // Неквадратные матрицы: 2x3 на 3x2 -> 2x2. Ловит перепутанные n/m/k (например, A.Rows вместо A.Cols).
    [Fact]
    public void Known_result_2x3_by_3x2()
    {
        var a = Matrix.FromArray(new long[,] { { 1, 2, 3 }, { 4, 5, 6 } });
        var b = Matrix.FromArray(new long[,] { { 7, 8 }, { 9, 10 }, { 11, 12 } });
        var expected = Matrix.FromArray(new long[,] { { 58, 64 }, { 139, 154 } });

        Reference.AssertSame(expected, DenseMultiplier.Multiply(a, b, 1));
        Reference.AssertSame(expected, DenseMultiplier.Multiply(a, b, 3));
    }

    // A * E = A. Единичная матрица — нейтральный элемент.
    [Fact]
    public void Multiply_by_identity_returns_same_matrix()
    {
        var a = Matrix.Generate(9, 9, new Random(1));
        Reference.AssertSame(a, DenseMultiplier.Multiply(a, Matrix.Identity(9), 4));
    }

    // A * 0 = 0. Ловит случай, когда результат не обнуляется / остаётся мусор.
    [Fact]
    public void Multiply_by_zero_gives_zero()
    {
        var a = Matrix.Generate(6, 5, new Random(2));
        var zero = new Matrix(5, 7);
        var c = DenseMultiplier.Multiply(a, zero, 3);
        Assert.Equal(6, c.Rows);
        Assert.Equal(7, c.Cols);
        Assert.All(c.Data, v => Assert.Equal(0L, v));
    }

    // Несовместимые размеры должны давать ошибку, а не тихий мусор или выход за границы массива.
    [Fact]
    public void Incompatible_sizes_throw()
    {
        var a = new Matrix(3, 4);
        var b = new Matrix(5, 2);
        Assert.Throws<ArgumentException>(() => DenseMultiplier.Multiply(a, b, 2));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Non_positive_thread_count_throws(int threads)
    {
        var a = Matrix.Generate(3, 3, new Random(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => DenseMultiplier.Multiply(a, a, threads));
    }

    // Многопоточный результат == эталон при разных T и ОБОИХ порядках циклов.
    // Размеры 17x13 и 13x11 нарочно не делятся на T=2,3,7: проверяется раздача остатка строк.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    public void Multithreaded_equals_reference(int threads)
    {
        var rng = new Random(10);
        var a = Matrix.Generate(17, 13, rng);
        var b = Matrix.Generate(13, 11, rng);
        Matrix expected = Reference.Multiply(a, b);

        Reference.AssertSame(expected, DenseMultiplier.Multiply(a, b, threads, LoopOrder.IKJ));
        Reference.AssertSame(expected, DenseMultiplier.Multiply(a, b, threads, LoopOrder.IJK));
    }

    // Потоков больше, чем строк: лишние потоки получают пустой диапазон и не должны ломать результат.
    [Fact]
    public void More_threads_than_rows_works()
    {
        var rng = new Random(11);
        var a = Matrix.Generate(3, 6, rng);
        var b = Matrix.Generate(6, 4, rng);
        Reference.AssertSame(Reference.Multiply(a, b), DenseMultiplier.Multiply(a, b, 10));
    }

    // Граничные случаи: 1x1, одна строка, один столбец.
    [Fact]
    public void Edge_cases_1x1_single_row_single_column()
    {
        var one = Matrix.FromArray(new long[,] { { 7 } });
        Reference.AssertSame(Matrix.FromArray(new long[,] { { 49 } }), DenseMultiplier.Multiply(one, one, 4));

        var rng = new Random(12);
        var row = Matrix.Generate(1, 5, rng);
        var m = Matrix.Generate(5, 4, rng);
        Reference.AssertSame(Reference.Multiply(row, m), DenseMultiplier.Multiply(row, m, 3));

        var m2 = Matrix.Generate(4, 5, rng);
        var col = Matrix.Generate(5, 1, rng);
        Reference.AssertSame(Reference.Multiply(m2, col), DenseMultiplier.Multiply(m2, col, 3));
    }

    // Прогресс в конце ровно 100%, все строки учтены, и "Прогресс: 100%" напечатан.
    [Fact]
    public void Progress_ends_at_exactly_100_percent()
    {
        var rng = new Random(13);
        var a = Matrix.Generate(50, 20, rng);
        var b = Matrix.Generate(20, 30, rng);
        var tracker = new ProgressTracker(a.Rows);
        var output = new StringWriter();

        DenseMultiplier.Multiply(a, b, 4, LoopOrder.IKJ, tracker, output);

        Assert.Equal(100, tracker.Percent);
        Assert.Equal((long)a.Rows, tracker.Done);
        Assert.Contains("Прогресс: 100%", output.ToString());
    }

    // Атомарность счётчика прогресса: 8 потоков по 100 000 увеличений должны дать ровно 800 000.
    // Если заменить Interlocked.Add на обычное "+=", тест начнёт (не всегда!) падать: потеряются обновления.
    [Fact]
    public void Progress_counter_is_thread_safe()
    {
        var tracker = new ProgressTracker(800_000);
        var threads = new Thread[8];
        for (int i = 0; i < threads.Length; i++)
        {
            threads[i] = new Thread(() =>
            {
                for (int j = 0; j < 100_000; j++) tracker.Add(1);
            });
        }
        foreach (Thread t in threads) t.Start();
        foreach (Thread t in threads) t.Join();

        Assert.Equal(800_000L, tracker.Done);
        Assert.Equal(100, tracker.Percent);
    }

    // Ошибки синхронизации проявляются не при каждом запуске, поэтому многопоточное умножение
    // выполняется 100 раз подряд, и каждый раз результат обязан быть одинаковым.
    [Fact]
    public void Repeated_100_times_always_same_result()
    {
        var rng = new Random(14);
        var a = Matrix.Generate(40, 30, rng);
        var b = Matrix.Generate(30, 25, rng);
        Matrix expected = Reference.Multiply(a, b);

        for (int rep = 0; rep < 100; rep++)
            Reference.AssertSame(expected, DenseMultiplier.Multiply(a, b, 4));
    }
}
