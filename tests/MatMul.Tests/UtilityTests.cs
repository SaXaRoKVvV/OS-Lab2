using MatMul.Core;

namespace MatMul.Tests;

public class PartitionTests
{
    // Блоки идут подряд без дыр и перекрытий, покрывают все строки, различаются не более чем на 1 строку.
    [Theory]
    [InlineData(10, 3)]
    [InlineData(3, 10)]    // потоков больше, чем строк
    [InlineData(0, 4)]
    [InlineData(7, 7)]
    [InlineData(100, 8)]
    public void Static_ranges_cover_all_rows_evenly(int rows, int threads)
    {
        var ranges = Partition.StaticRanges(rows, threads);

        Assert.Equal(threads, ranges.Length);
        Assert.Equal(0, ranges[0].Start);
        Assert.Equal(rows, ranges[threads - 1].End);
        for (int t = 1; t < threads; t++)
            Assert.Equal(ranges[t - 1].End, ranges[t].Start);

        int min = ranges.Min(r => r.End - r.Start);
        int max = ranges.Max(r => r.End - r.Start);
        Assert.True(max - min <= 1);
    }
}

public class RowDispenserTests
{
    // 8 потоков одновременно берут порции по 5 строк из 1003. Каждая строка должна быть выдана ровно один раз.
    [Fact]
    public void Concurrent_threads_get_each_row_exactly_once()
    {
        const int total = 1003;
        var dispenser = new RowDispenser(total, 5);
        var hits = new int[total];

        var threads = new Thread[8];
        for (int i = 0; i < threads.Length; i++)
        {
            threads[i] = new Thread(() =>
            {
                while (dispenser.TryTake(out int start, out int end))
                {
                    for (int row = start; row < end; row++)
                        Interlocked.Increment(ref hits[row]);
                }
            });
        }
        foreach (Thread t in threads) t.Start();
        foreach (Thread t in threads) t.Join();

        Assert.All(hits, h => Assert.Equal(1, h));
    }

    // Последняя порция обрезается по границе, после конца всегда false.
    [Fact]
    public void Last_chunk_is_clipped_and_then_exhausted()
    {
        var dispenser = new RowDispenser(7, 5);
        Assert.True(dispenser.TryTake(out int s1, out int e1));
        Assert.Equal((0, 5), (s1, e1));
        Assert.True(dispenser.TryTake(out int s2, out int e2));
        Assert.Equal((5, 7), (s2, e2));
        Assert.False(dispenser.TryTake(out _, out _));
        Assert.False(dispenser.TryTake(out _, out _));
    }
}

public class StatsTests
{
    // Отбрасываем минимум (-50) и максимум (100): среднее от (1, 2, 3) = 2.
    [Fact]
    public void Trimmed_mean_drops_min_and_max()
    {
        Assert.Equal(2.0, Stats.TrimmedMean(new List<double> { 1, 100, 2, 3, -50 }));
    }

    [Fact]
    public void Trimmed_mean_with_fewer_than_three_values_is_plain_mean()
    {
        Assert.Equal(3.0, Stats.TrimmedMean(new List<double> { 2, 4 }));
        Assert.Equal(5.0, Stats.TrimmedMean(new List<double> { 5 }));
    }
}

public class IpcFormatTests
{
    // "Принятые матрицы совпадают с отправленными": запись в поток и чтение обратно.
    [Fact]
    public void Dense_pair_roundtrip()
    {
        var rng = new Random(30);
        var a = Matrix.Generate(7, 5, rng);
        var b = Matrix.Generate(5, 9, rng);

        var stream = new MemoryStream();
        MatrixIo.WriteDensePair(stream, a, b, 123456);
        stream.Position = 0;
        var received = MatrixIo.ReadDensePair(stream);

        Assert.True(a.ContentEquals(received.A));
        Assert.True(b.ContentEquals(received.B));
        Assert.Equal(123456L, received.GenerationMicros);
        Assert.Equal(stream.Length, stream.Position);   // прочитано ровно всё, ничего не осталось
    }

    [Fact]
    public void Triangular_pair_roundtrip_keeps_row_lengths()
    {
        var rng = new Random(31);
        var a = TriangularMatrix.Generate(12, rng);
        var b = Matrix.Generate(12, 3, rng);

        var stream = new MemoryStream();
        MatrixIo.WriteTriangularPair(stream, a, b, 5);
        stream.Position = 0;
        var received = MatrixIo.ReadTriangularPair(stream);

        Assert.Equal(a.N, received.A.N);
        for (int i = 0; i < a.N; i++)
        {
            Assert.Equal(i + 1, received.A.Rows[i].Length);
            Assert.Equal(a.Rows[i], received.A.Rows[i]);
        }
        Assert.True(b.ContentEquals(received.B));
    }

    // Мусор вместо заголовка должен быть распознан, а не прочитан как матрица.
    [Fact]
    public void Wrong_header_is_rejected()
    {
        var stream = new MemoryStream(new byte[64]);
        Assert.Throws<InvalidDataException>(() => MatrixIo.ReadDensePair(stream));
    }

    // Передача оборвалась на середине (например, генератор упал): получатель обязан заметить, а не вернуть недописанную матрицу.
    [Fact]
    public void Truncated_stream_is_detected()
    {
        var rng = new Random(32);
        var a = Matrix.Generate(10, 10, rng);
        var b = Matrix.Generate(10, 10, rng);
        var stream = new MemoryStream();
        MatrixIo.WriteDensePair(stream, a, b, 1);

        byte[] bytes = stream.ToArray();
        var cut = new MemoryStream(bytes, 0, bytes.Length - 20);
        Assert.Throws<EndOfStreamException>(() => MatrixIo.ReadDensePair(cut));
    }

    // Нельзя прочитать сообщение с треугольной матрицей как с обычными.
    [Fact]
    public void Wrong_message_kind_is_rejected()
    {
        var rng = new Random(33);
        var stream = new MemoryStream();
        MatrixIo.WriteTriangularPair(stream, TriangularMatrix.Generate(3, rng), Matrix.Generate(3, 3, rng), 0);
        stream.Position = 0;
        Assert.Throws<InvalidDataException>(() => MatrixIo.ReadDensePair(stream));
    }
}
