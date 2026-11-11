namespace MatMul.Core;

/// <summary>
/// Нижнетреугольная матрица N x N. Элементы выше главной диагонали равны нулю и НЕ хранятся.
/// Строка i содержит i + 1 элементов: A[i][0] ... A[i][i]
/// (массив массивов разной длины).
/// </summary>
public sealed class TriangularMatrix
{
    public int N { get; }
    public long[][] Rows { get; }

    public TriangularMatrix(int n)
    {
        if (n < 0) throw new ArgumentOutOfRangeException(nameof(n));
        N = n;
        Rows = new long[n][];
        for (int i = 0; i < n; i++)
            Rows[i] = new long[i + 1];
    }

    public TriangularMatrix(long[][] rows)
    {
        for (int i = 0; i < rows.Length; i++)
        {
            if (rows[i].Length != i + 1)
                throw new ArgumentException(
                    $"Строка {i} должна содержать {i + 1} элементов, а содержит {rows[i].Length}");
        }
        N = rows.Length;
        Rows = rows;
    }

    /// <summary>Элемент (i, j); выше диагонали всегда 0.</summary>
    public long this[int i, int j] => j > i ? 0 : Rows[i][j];

    public static TriangularMatrix Generate(int n, Random rng, int maxExclusive = 10)
    {
        var m = new TriangularMatrix(n);
        for (int i = 0; i < n; i++)
            for (int j = 0; j <= i; j++)
                m.Rows[i][j] = rng.Next(maxExclusive);
        return m;
    }

    /// <summary>Развернуть в обычную N x N матрицу (для тестов).</summary>
    public Matrix ToDense()
    {
        var m = new Matrix(N, N);
        for (int i = 0; i < N; i++)
            for (int j = 0; j <= i; j++)
                m[i, j] = Rows[i][j];
        return m;
    }
}
