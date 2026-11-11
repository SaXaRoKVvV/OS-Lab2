using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace MatMul.Core;

/// <summary>
/// Двоичный формат передачи матриц между процессами (через любой Stream: канал, файл, память).
///
/// Сообщение:  [magic:int32]['kind':int32][матрица A][матрица B][generationMicros:int64]
///   kind 0 — обе матрицы обычные, kind 1 — A треугольная.
/// Обычная матрица:       [rows:int32][cols:int32][rows*cols значений int64]
/// Треугольная матрица:   [n:int32][строка 0 (1 знач.)][строка 1 (2 знач.)]...[строка n-1 (n знач.)]
/// Все числа — little-endian. Размеры идут ПЕРЕД данными, поэтому получатель точно знает,
/// сколько байт читать, и понимает, что матрица принята целиком.
/// </summary>
public static class MatrixIo
{
    private const int Magic = 0x5254414D;   // байты 'M','A','T','R'
    private const int KindDense = 0;
    private const int KindTriangular = 1;

    // ---------- сообщения целиком ----------

    public static void WriteDensePair(Stream s, Matrix a, Matrix b, long generationMicros)
    {
        WriteInt32(s, Magic);
        WriteInt32(s, KindDense);
        WriteMatrix(s, a);
        WriteMatrix(s, b);
        WriteInt64(s, generationMicros);
    }

    public static (Matrix A, Matrix B, long GenerationMicros) ReadDensePair(Stream s)
    {
        ReadHeader(s, KindDense);
        Matrix a = ReadMatrix(s);
        Matrix b = ReadMatrix(s);
        long micros = ReadInt64(s);
        return (a, b, micros);
    }

    public static void WriteTriangularPair(Stream s, TriangularMatrix a, Matrix b, long generationMicros)
    {
        WriteInt32(s, Magic);
        WriteInt32(s, KindTriangular);
        WriteTriangular(s, a);
        WriteMatrix(s, b);
        WriteInt64(s, generationMicros);
    }

    public static (TriangularMatrix A, Matrix B, long GenerationMicros) ReadTriangularPair(Stream s)
    {
        ReadHeader(s, KindTriangular);
        TriangularMatrix a = ReadTriangular(s);
        Matrix b = ReadMatrix(s);
        long micros = ReadInt64(s);
        return (a, b, micros);
    }

    // ---------- отдельные матрицы ----------

    public static void WriteMatrix(Stream s, Matrix m)
    {
        EnsureLittleEndian();
        WriteInt32(s, m.Rows);
        WriteInt32(s, m.Cols);
        s.Write(MemoryMarshal.AsBytes(m.Data.AsSpan()));
    }

    public static Matrix ReadMatrix(Stream s)
    {
        EnsureLittleEndian();
        int rows = ReadInt32(s);
        int cols = ReadInt32(s);
        if (rows < 0 || cols < 0 || (long)rows * cols > int.MaxValue / 8)
            throw new InvalidDataException($"Недопустимые размеры матрицы: {rows}x{cols}");
        var m = new Matrix(rows, cols);
        s.ReadExactly(MemoryMarshal.AsBytes(m.Data.AsSpan()));
        return m;
    }

    public static void WriteTriangular(Stream s, TriangularMatrix t)
    {
        EnsureLittleEndian();
        WriteInt32(s, t.N);
        for (int i = 0; i < t.N; i++)
            s.Write(MemoryMarshal.AsBytes(t.Rows[i].AsSpan()));
    }

    public static TriangularMatrix ReadTriangular(Stream s)
    {
        EnsureLittleEndian();
        int n = ReadInt32(s);
        if (n < 0 || n > 100_000)
            throw new InvalidDataException($"Недопустимый размер треугольной матрицы: {n}");
        var t = new TriangularMatrix(n);
        for (int i = 0; i < n; i++)
            s.ReadExactly(MemoryMarshal.AsBytes(t.Rows[i].AsSpan()));
        return t;
    }

    // ---------- вспомогательное ----------

    private static void ReadHeader(Stream s, int expectedKind)
    {
        int magic = ReadInt32(s);
        if (magic != Magic)
            throw new InvalidDataException("Неверный заголовок сообщения (не матрицы от генератора?)");
        int kind = ReadInt32(s);
        if (kind != expectedKind)
            throw new InvalidDataException($"Неожиданный тип сообщения: {kind}, ожидался {expectedKind}");
    }

    private static void EnsureLittleEndian()
    {
        if (!BitConverter.IsLittleEndian)
            throw new PlatformNotSupportedException("Формат рассчитан на little-endian процессоры");
    }

    private static void WriteInt32(Stream s, int value)
    {
        Span<byte> buf = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(buf, value);
        s.Write(buf);
    }

    private static int ReadInt32(Stream s)
    {
        Span<byte> buf = stackalloc byte[4];
        s.ReadExactly(buf);
        return BinaryPrimitives.ReadInt32LittleEndian(buf);
    }

    private static void WriteInt64(Stream s, long value)
    {
        Span<byte> buf = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(buf, value);
        s.Write(buf);
    }

    private static long ReadInt64(Stream s)
    {
        Span<byte> buf = stackalloc byte[8];
        s.ReadExactly(buf);
        return BinaryPrimitives.ReadInt64LittleEndian(buf);
    }
}
