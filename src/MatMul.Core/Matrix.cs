namespace MatMul.Core;

/// <summary>
/// Обычная (плотная) матрица. Все элементы лежат в ОДНОМ массиве long[] по строкам:
/// элемент (i, j) находится в Data[i * Cols + j].
/// Тип long (64 бита) выбран, чтобы суммы произведений не переполнялись.
/// </summary>
public sealed class Matrix
{
    public int Rows { get; }
    public int Cols { get; }
    public long[] Data { get; }

    public Matrix(int rows, int cols)
    {
        if (rows < 0) throw new ArgumentOutOfRangeException(nameof(rows));
        if (cols < 0) throw new ArgumentOutOfRangeException(nameof(cols));
        Rows = rows;
        Cols = cols;
        Data = new long[checked(rows * cols)];
    }

    public Matrix(int rows, int cols, long[] data)
    {
        if (rows < 0) throw new ArgumentOutOfRangeException(nameof(rows));
        if (cols < 0) throw new ArgumentOutOfRangeException(nameof(cols));
        if (data.Length != rows * cols)
            throw new ArgumentException("Длина массива не равна rows * cols");
        Rows = rows;
        Cols = cols;
        Data = data;
    }

    public long this[int i, int j]
    {
        get => Data[i * Cols + j];
        set => Data[i * Cols + j] = value;
    }

    /// <summary>Создать матрицу из двумерного массива (удобно в тестах).</summary>
    public static Matrix FromArray(long[,] values)
    {
        int rows = values.GetLength(0);
        int cols = values.GetLength(1);
        var m = new Matrix(rows, cols);
        for (int i = 0; i < rows; i++)
            for (int j = 0; j < cols; j++)
                m.Data[i * cols + j] = values[i, j];
        return m;
    }

    /// <summary>Матрица со случайными целыми от 0 до maxExclusive-1.</summary>
    public static Matrix Generate(int rows, int cols, Random rng, int maxExclusive = 10)
    {
        var m = new Matrix(rows, cols);
        for (int i = 0; i < m.Data.Length; i++)
            m.Data[i] = rng.Next(maxExclusive);
        return m;
    }

    public static Matrix Identity(int n)
    {
        var m = new Matrix(n, n);
        for (int i = 0; i < n; i++)
            m.Data[i * n + i] = 1;
        return m;
    }

    /// <summary>Контрольная сумма: сумма всех элементов.</summary>
    public long Checksum()
    {
        long sum = 0;
        foreach (long v in Data)
            sum = unchecked(sum + v);
        return sum;
    }

    public bool ContentEquals(Matrix other)
    {
        return Rows == other.Rows && Cols == other.Cols
            && Data.AsSpan().SequenceEqual(other.Data.AsSpan());
    }

    public void Print(TextWriter writer)
    {
        for (int i = 0; i < Rows; i++)
        {
            for (int j = 0; j < Cols; j++)
            {
                if (j > 0) writer.Write(' ');
                writer.Write(Data[i * Cols + j]);
            }
            writer.WriteLine();
        }
    }
}
