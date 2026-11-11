using MatMul.Core;

namespace MatMul.Tests;

public class TriangularTests
{
    // Строка i хранит ровно i+1 элементов; выше диагонали читается ноль.
    [Fact]
    public void Rows_have_increasing_length_and_zeros_above_diagonal()
    {
        var a = TriangularMatrix.Generate(6, new Random(1));
        for (int i = 0; i < a.N; i++)
        {
            Assert.Equal(i + 1, a.Rows[i].Length);
            for (int j = i + 1; j < a.N; j++)
                Assert.Equal(0L, a[i, j]);
        }
    }

    [Fact]
    public void Constructor_rejects_wrong_row_lengths()
    {
        var bad = new long[][] { new long[] { 1 }, new long[] { 1, 2, 3 } };
        Assert.Throws<ArgumentException>(() => new TriangularMatrix(bad));
    }

    // Известный ответ: A = [[1,0],[2,3]], B = [[4,5],[6,7]]  ->  C = [[4,5],[26,31]].
    [Fact]
    public void Known_result_2x2()
    {
        var a = new TriangularMatrix(new long[][] { new long[] { 1 }, new long[] { 2, 3 } });
        var b = Matrix.FromArray(new long[,] { { 4, 5 }, { 6, 7 } });
        var expected = Matrix.FromArray(new long[,] { { 4, 5 }, { 26, 31 } });

        Reference.AssertSame(expected, TriangularMultiplier.Multiply(a, b, 2, Distribution.Static));
        Reference.AssertSame(expected, TriangularMultiplier.Multiply(a, b, 2, Distribution.Dynamic));
    }

    // Статический и динамический способы дают ТОТ ЖЕ результат, что и обычное умножение
    // развёрнутой (с нулями) матрицы. Разные T, в том числе больше числа строк, и разные порции.
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(3, 4)]
    [InlineData(7, 1)]
    [InlineData(7, 64)]
    [InlineData(50, 3)]    // потоков больше, чем строк (n = 23)
    public void Static_and_dynamic_equal_reference(int threads, int chunk)
    {
        var rng = new Random(20);
        var a = TriangularMatrix.Generate(23, rng);
        var b = Matrix.Generate(23, 9, rng);
        Matrix expected = Reference.Multiply(a.ToDense(), b);

        Reference.AssertSame(expected, TriangularMultiplier.Multiply(a, b, threads, Distribution.Static, chunk));
        Reference.AssertSame(expected, TriangularMultiplier.Multiply(a, b, threads, Distribution.Dynamic, chunk));
    }

    // Каждая строка посчитана РОВНО один раз. Ловит и "пропущенные" строки (ошибка в границах блоков),
    // и "посчитанные дважды" (ошибка в динамическом счётчике — например, неатомарное взятие порции).
    [Theory]
    [InlineData(Distribution.Static, 1)]
    [InlineData(Distribution.Dynamic, 1)]
    [InlineData(Distribution.Dynamic, 5)]
    [InlineData(Distribution.Dynamic, 64)]
    public void Every_row_is_computed_exactly_once(Distribution distribution, int chunk)
    {
        var rng = new Random(21);
        var a = TriangularMatrix.Generate(200, rng);
        var b = Matrix.Generate(200, 4, rng);

        for (int rep = 0; rep < 20; rep++)
        {
            var hits = new int[a.N];
            TriangularMultiplier.Multiply(a, b, 8, distribution, chunk, null, null, hits);
            Assert.All(hits, h => Assert.Equal(1, h));
        }
    }

    [Fact]
    public void Progress_ends_at_100_percent_for_both_distributions()
    {
        var rng = new Random(22);
        var a = TriangularMatrix.Generate(60, rng);
        var b = Matrix.Generate(60, 5, rng);

        foreach (Distribution d in new[] { Distribution.Static, Distribution.Dynamic })
        {
            var tracker = new ProgressTracker(a.N);
            TriangularMultiplier.Multiply(a, b, 5, d, 2, tracker, null);
            Assert.Equal(100, tracker.Percent);
            Assert.Equal((long)a.N, tracker.Done);
        }
    }

    [Fact]
    public void Incompatible_sizes_throw()
    {
        var a = new TriangularMatrix(4);
        var b = new Matrix(5, 3);
        Assert.Throws<ArgumentException>(() => TriangularMultiplier.Multiply(a, b, 2, Distribution.Static));
    }
}
