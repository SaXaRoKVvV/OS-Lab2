using MatMul.Core;

namespace MatMul.Tests;

/// <summary>
/// Эталон: самая простая однопоточная реализация "в лоб" (три цикла).
/// С ней сравниваются все многопоточные результаты. Она намеренно не использует
/// код из MatMul.Core, чтобы ошибка в основной программе не могла "скрыть" сама себя.
/// </summary>
internal static class Reference
{
    public static Matrix Multiply(Matrix a, Matrix b)
    {
        var c = new Matrix(a.Rows, b.Cols);
        for (int i = 0; i < a.Rows; i++)
        {
            for (int j = 0; j < b.Cols; j++)
            {
                long sum = 0;
                for (int t = 0; t < a.Cols; t++)
                    sum += a[i, t] * b[t, j];
                c[i, j] = sum;
            }
        }
        return c;
    }

    public static void AssertSame(Matrix expected, Matrix actual)
    {
        Assert.Equal(expected.Rows, actual.Rows);
        Assert.Equal(expected.Cols, actual.Cols);
        Assert.Equal(expected.Data, actual.Data);
    }
}
