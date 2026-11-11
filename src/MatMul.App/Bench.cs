using System.Diagnostics;
using System.Globalization;
using MatMul.Core;

namespace MatMul.App;

internal sealed record Measurement(string Experiment, int Size, int Threads, string Variant, int Run, double Seconds);

internal sealed record SummaryRow(string Experiment, int Size, string Variant, int Threads,
                                  double MeanSeconds, double? Speedup, double? Efficiency);

/// <summary>
/// Серии замеров по методике из раздела 6 задания:
///  - замеряется только умножение (генерация матриц — до таймера);
///  - монотонный таймер Stopwatch;
///  - несколько замеров на комбинацию, среднее без максимума и минимума;
///  - перед серией — прогревочное умножение.
/// Каждый замер сразу дописывается в CSV, поэтому при прерывании ничего не теряется.
/// </summary>
internal static class Bench
{
    private const string RawHeader = "experiment,size,threads,variant,run,seconds";
    private const string SummaryHeader = "experiment,size,variant,threads,mean_seconds,speedup,efficiency";

    // ---------- bench-mul: плотное умножение, зависимость от числа потоков ----------
    public static int Mul(Args args)
    {
        List<int> sizes = args.GetIntList("sizes", new[] { 500, 1000, 2000 });
        List<int> threadList = args.GetIntList("threads", DefaultThreadList());
        int runs = args.GetInt("runs", 5);
        string prefix = args.Get("out", "mul");
        ValidatePositive(sizes, "--sizes");
        ValidatePositive(threadList, "--threads");
        ValidatePositive(new List<int> { runs }, "--runs");

        var all = new List<Measurement>();
        string rawPath = StartRawFile(prefix);
        Warmup();

        foreach (int size in sizes)
        {
            var rng = new Random(42);
            Matrix a = Matrix.Generate(size, size, rng);
            Matrix b = Matrix.Generate(size, size, rng);
            var guard = new ChecksumGuard();

            foreach (int t in threadList)
            {
                for (int run = 1; run <= runs; run++)
                {
                    ForceGc();
                    var sw = Stopwatch.StartNew();
                    Matrix c = DenseMultiplier.Multiply(a, b, t, LoopOrder.IKJ);
                    sw.Stop();
                    guard.Check(c.Checksum(), $"n={size}, T={t}");
                    Record(all, rawPath, new Measurement("mul", size, t, "ikj", run, sw.Elapsed.TotalSeconds));
                }
            }
        }

        Finish(prefix, all);
        return 0;
    }

    // ---------- bench-order: сравнение порядка циклов, один поток ----------
    public static int Order(Args args)
    {
        int n = args.GetInt("n", 2000);
        int runs = args.GetInt("runs", 5);
        string prefix = args.Get("out", "order");
        ValidatePositive(new List<int> { n, runs }, "--n/--runs");

        var all = new List<Measurement>();
        string rawPath = StartRawFile(prefix);
        Warmup();

        var rng = new Random(42);
        Matrix a = Matrix.Generate(n, n, rng);
        Matrix b = Matrix.Generate(n, n, rng);
        var guard = new ChecksumGuard();

        foreach (LoopOrder order in new[] { LoopOrder.IKJ, LoopOrder.IJK })
        {
            for (int run = 1; run <= runs; run++)
            {
                ForceGc();
                var sw = Stopwatch.StartNew();
                Matrix c = DenseMultiplier.Multiply(a, b, 1, order);
                sw.Stop();
                guard.Check(c.Checksum(), $"order={order}");
                Record(all, rawPath, new Measurement("order", n, 1, order.ToString().ToLowerInvariant(), run, sw.Elapsed.TotalSeconds));
            }
        }

        Finish(prefix, all);
        return 0;
    }

    // ---------- bench-tri: треугольная матрица, статическое и динамическое распределение ----------
    public static int Tri(Args args)
    {
        List<int> sizes = args.GetIntList("sizes", new[] { 500, 1000, 2000 });
        List<int> threadList = args.GetIntList("threads", DefaultThreadList());
        List<int> chunks = args.GetIntList("chunks", new[] { 1 });
        int runs = args.GetInt("runs", 5);
        string prefix = args.Get("out", "tri");
        ValidatePositive(sizes, "--sizes");
        ValidatePositive(threadList, "--threads");
        ValidatePositive(chunks, "--chunks");
        ValidatePositive(new List<int> { runs }, "--runs");

        // варианты: статический + динамический с каждым размером порции
        var variants = new List<(string Name, Distribution Mode, int Chunk)>();
        variants.Add(("static", Distribution.Static, 1));
        foreach (int chunk in chunks)
            variants.Add(("dynamic-c" + chunk, Distribution.Dynamic, chunk));

        var all = new List<Measurement>();
        string rawPath = StartRawFile(prefix);
        Warmup();

        foreach (int size in sizes)
        {
            var rng = new Random(42);
            TriangularMatrix a = TriangularMatrix.Generate(size, rng);
            Matrix b = Matrix.Generate(size, size, rng);
            var guard = new ChecksumGuard();

            foreach (int t in threadList)
            {
                foreach (var variant in variants)
                {
                    for (int run = 1; run <= runs; run++)
                    {
                        ForceGc();
                        var sw = Stopwatch.StartNew();
                        Matrix c = TriangularMultiplier.Multiply(a, b, t, variant.Mode, variant.Chunk);
                        sw.Stop();
                        guard.Check(c.Checksum(), $"n={size}, T={t}, {variant.Name}");
                        Record(all, rawPath, new Measurement("tri", size, t, variant.Name, run, sw.Elapsed.TotalSeconds));
                    }
                }
            }
        }

        Finish(prefix, all);
        return 0;
    }

    // ---------- bench-ipc: время передачи матриц между процессами и время умножения ----------
    public static int Ipc(Args args)
    {
        List<int> sizes = args.GetIntList("sizes", new[] { 500, 1000, 2000 });
        int threads = args.GetInt("threads", Environment.ProcessorCount);
        int runs = args.GetInt("runs", 5);
        string prefix = args.Get("out", "ipc");
        ValidatePositive(sizes, "--sizes");
        ValidatePositive(new List<int> { threads, runs }, "--threads/--runs");

        var all = new List<Measurement>();
        string rawPath = StartRawFile(prefix);
        Warmup();

        foreach (int size in sizes)
        {
            for (int run = 1; run <= runs; run++)
            {
                var received = GeneratorClient.ReceiveDense(size, size, size, 42);
                // "Передача" = всё время получения минус время генерации внутри процесса-генератора.
                // В него входит также запуск дочернего процесса (старт среды выполнения .NET).
                double transfer = Math.Max(0.0, received.TotalSeconds - received.GenerationSeconds);

                ForceGc();
                var sw = Stopwatch.StartNew();
                Matrix c = DenseMultiplier.Multiply(received.A, received.B, threads, LoopOrder.IKJ);
                sw.Stop();
                _ = c.Checksum();

                Record(all, rawPath, new Measurement("ipc", size, threads, "receive_total", run, received.TotalSeconds));
                Record(all, rawPath, new Measurement("ipc", size, threads, "generation", run, received.GenerationSeconds));
                Record(all, rawPath, new Measurement("ipc", size, threads, "transfer", run, transfer));
                Record(all, rawPath, new Measurement("ipc", size, threads, "multiply", run, sw.Elapsed.TotalSeconds));
            }
        }

        Finish(prefix, all);
        return 0;
    }

    // ---------- общее ----------

    /// <summary>
    /// Числа потоков по умолчанию: 1..L (шаг 1, L — число логических процессоров),
    /// затем 1.25L, 1.5L, 2L, 4L, 8L.
    /// </summary>
    public static List<int> DefaultThreadList()
    {
        int l = Environment.ProcessorCount;
        var set = new SortedSet<int>();
        for (int t = 1; t <= l; t++) set.Add(t);
        set.Add((int)Math.Round(l * 1.25));
        set.Add((int)Math.Round(l * 1.5));
        set.Add(l * 2);
        set.Add(l * 4);
        set.Add(l * 8);
        return new List<int>(set);
    }

    /// <summary>Небольшое неучитываемое умножение, чтобы всё успело скомпилироваться JIT-ом.</summary>
    private static void Warmup()
    {
        var rng = new Random(1);
        Matrix a = Matrix.Generate(200, 200, rng);
        Matrix b = Matrix.Generate(200, 200, rng);
        TriangularMatrix ta = TriangularMatrix.Generate(200, rng);

        DenseMultiplier.Multiply(a, b, 2, LoopOrder.IKJ);
        DenseMultiplier.Multiply(a, b, 1, LoopOrder.IJK);
        TriangularMultiplier.Multiply(ta, b, 2, Distribution.Static);
        TriangularMultiplier.Multiply(ta, b, 2, Distribution.Dynamic, 4);
    }

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static void ValidatePositive(List<int> values, string name)
    {
        foreach (int v in values)
            if (v < 1) throw new ArgumentException($"{name}: все значения должны быть >= 1");
    }

    private static string StartRawFile(string prefix)
    {
        string path = prefix + "_raw.csv";
        File.WriteAllText(path, RawHeader + Environment.NewLine);
        return path;
    }

    private static void Record(List<Measurement> all, string rawPath, Measurement m)
    {
        all.Add(m);
        File.AppendAllText(rawPath,
            FormattableString.Invariant($"{m.Experiment},{m.Size},{m.Threads},{m.Variant},{m.Run},{m.Seconds:F6}")
            + Environment.NewLine);
        Console.WriteLine(FormattableString.Invariant(
            $"[{m.Experiment}] n={m.Size} T={m.Threads} {m.Variant} замер {m.Run}: {m.Seconds:F3} с"));
    }

    private static void Finish(string prefix, List<Measurement> all)
    {
        List<SummaryRow> summary = BuildSummary(all);
        string path = prefix + "_summary.csv";

        var lines = new List<string> { SummaryHeader };
        foreach (SummaryRow r in summary)
        {
            lines.Add(FormattableString.Invariant(
                $"{r.Experiment},{r.Size},{r.Variant},{r.Threads},{r.MeanSeconds:F6},{Fmt(r.Speedup)},{Fmt(r.Efficiency)}"));
        }
        File.WriteAllLines(path, lines);

        Console.WriteLine();
        Console.WriteLine("Итог (среднее без самого большого и самого маленького замера):");
        foreach (SummaryRow r in summary)
        {
            string extra = r.Speedup.HasValue
                ? $"S={r.Speedup.Value:F2}  E={r.Efficiency.GetValueOrDefault():F2}"
                : "";
            Console.WriteLine($"  [{r.Experiment}] n={r.Size} {r.Variant,-14} T={r.Threads,-4} {r.MeanSeconds,9:F3} с  {extra}");
        }
        Console.WriteLine();
        Console.WriteLine($"Сырые замеры: {prefix}_raw.csv; усреднённые: {path}");
    }

    private static string Fmt(double? value)
    {
        return value.HasValue ? value.Value.ToString("F4", CultureInfo.InvariantCulture) : "";
    }

    /// <summary>
    /// Для каждой комбинации (эксперимент, размер, вариант) считает среднее по потокам,
    /// ускорение S(T) = t(1) / t(T) и эффективность E(T) = S(T) / T.
    /// Ускорение считается только если в серии есть T = 1 и ещё хотя бы одно значение T.
    /// </summary>
    private static List<SummaryRow> BuildSummary(List<Measurement> all)
    {
        var rows = new List<SummaryRow>();
        var keys = all.Select(m => (m.Experiment, m.Size, m.Variant)).Distinct().ToList();

        foreach (var key in keys)
        {
            List<Measurement> group = all
                .Where(m => m.Experiment == key.Experiment && m.Size == key.Size && m.Variant == key.Variant)
                .ToList();
            List<int> threadValues = group.Select(m => m.Threads).Distinct().OrderBy(x => x).ToList();

            var means = new List<(int Threads, double Mean)>();
            foreach (int t in threadValues)
            {
                List<double> values = group.Where(m => m.Threads == t).Select(m => m.Seconds).ToList();
                means.Add((t, Stats.TrimmedMean(values)));
            }

            double? baseline = null;
            if (threadValues.Count > 1 && means[0].Threads == 1)
                baseline = means[0].Mean;

            foreach (var entry in means)
            {
                double? speedup = null;
                double? efficiency = null;
                if (baseline.HasValue)
                {
                    speedup = baseline.Value / entry.Mean;
                    efficiency = speedup.Value / entry.Threads;
                }
                rows.Add(new SummaryRow(key.Experiment, key.Size, key.Variant, entry.Threads, entry.Mean, speedup, efficiency));
            }
        }
        return rows;
    }

    /// <summary>
    /// Защита от гонок и от "выброшенных" вычислений: контрольная сумма C
    /// должна быть одинаковой при любом числе потоков.
    /// </summary>
    private sealed class ChecksumGuard
    {
        private long? _expected;

        public void Check(long actual, string context)
        {
            if (_expected == null)
            {
                _expected = actual;
            }
            else if (_expected.Value != actual)
            {
                Console.WriteLine($"!!! ВНИМАНИЕ: контрольная сумма {actual} не равна {_expected.Value} ({context}) — ошибка в вычислениях");
            }
        }
    }
}
