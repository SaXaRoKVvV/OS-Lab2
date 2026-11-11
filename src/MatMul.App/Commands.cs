using System.Diagnostics;
using MatMul.Core;

namespace MatMul.App;

internal static class Commands
{
    // matmul mul n m k T [--order ijk|ikj] [--ipc] [--verify] [--no-progress] [--seed S]
    public static int Mul(Args args)
    {
        int n = args.PositionalInt(0, "n (строк в A)");
        int m = args.PositionalInt(1, "m (столбцов в A = строк в B)");
        int k = args.PositionalInt(2, "k (столбцов в B)");
        int threads = args.PositionalInt(3, "T (число потоков)");
        RequirePositive(n, "n");
        RequirePositive(m, "m");
        RequirePositive(k, "k");
        RequirePositive(threads, "T");

        int seed = args.GetInt("seed", 12345);
        LoopOrder order = ParseOrder(args.Get("order", "ikj"));
        bool showProgress = !args.Has("no-progress");

        Console.WriteLine($"A: {n}x{m}, B: {m}x{k}, потоков: {threads}, порядок циклов: {order}");

        Matrix a;
        Matrix b;
        if (args.Has("ipc"))
        {
            var received = GeneratorClient.ReceiveDense(n, m, k, seed);
            a = received.A;
            b = received.B;
            Console.WriteLine(
                $"Матрицы получены от процесса-генератора за {received.TotalSeconds:F3} с " +
                $"(из них генерация {received.GenerationSeconds:F3} с)");
        }
        else
        {
            var rng = new Random(seed);
            a = Matrix.Generate(n, m, rng);   // время генерации в замер не входит
            b = Matrix.Generate(m, k, rng);
        }

        var sw = Stopwatch.StartNew();
        Matrix c = DenseMultiplier.Multiply(a, b, threads, order, null, showProgress ? Console.Out : null);
        sw.Stop();

        Console.WriteLine($"Время умножения: {sw.Elapsed.TotalSeconds:F3} с");
        Console.WriteLine($"Контрольная сумма C: {c.Checksum()}");
        if (n <= 10 && k <= 10)
        {
            Console.WriteLine("Матрица C:");
            c.Print(Console.Out);
        }

        if (args.Has("verify"))
        {
            Matrix reference = DenseMultiplier.Multiply(a, b, 1, LoopOrder.IJK);
            Console.WriteLine(reference.ContentEquals(c)
                ? "Проверка: результат совпадает с последовательным (1 поток, порядок ijk) — OK"
                : "Проверка: РЕЗУЛЬТАТ НЕ СОВПАДАЕТ с последовательным!");
        }
        return 0;
    }

    // matmul tri n k T static|dynamic [--chunk C] [--ipc] [--verify] [--no-progress] [--seed S]
    public static int Tri(Args args)
    {
        int n = args.PositionalInt(0, "n (размер A)");
        int k = args.PositionalInt(1, "k (столбцов в B)");
        int threads = args.PositionalInt(2, "T (число потоков)");
        string mode = args.PositionalString(3, "static или dynamic").ToLowerInvariant();
        RequirePositive(n, "n");
        RequirePositive(k, "k");
        RequirePositive(threads, "T");

        Distribution distribution;
        if (mode == "static") distribution = Distribution.Static;
        else if (mode == "dynamic") distribution = Distribution.Dynamic;
        else throw new ArgumentException("Способ распределения: static или dynamic");

        int chunk = args.GetInt("chunk", 1);
        RequirePositive(chunk, "--chunk");
        int seed = args.GetInt("seed", 12345);
        bool showProgress = !args.Has("no-progress");

        Console.WriteLine($"A: нижнетреугольная {n}x{n}, B: {n}x{k}, потоков: {threads}, распределение: {distribution}" +
                          (distribution == Distribution.Dynamic ? $", порция: {chunk} строк" : ""));

        TriangularMatrix a;
        Matrix b;
        if (args.Has("ipc"))
        {
            var received = GeneratorClient.ReceiveTriangular(n, k, seed);
            a = received.A;
            b = received.B;
            Console.WriteLine(
                $"Матрицы получены от процесса-генератора за {received.TotalSeconds:F3} с " +
                $"(из них генерация {received.GenerationSeconds:F3} с)");
        }
        else
        {
            var rng = new Random(seed);
            a = TriangularMatrix.Generate(n, rng);
            b = Matrix.Generate(n, k, rng);
        }

        var sw = Stopwatch.StartNew();
        Matrix c = TriangularMultiplier.Multiply(a, b, threads, distribution, chunk,
                                                 null, showProgress ? Console.Out : null);
        sw.Stop();

        Console.WriteLine($"Время умножения: {sw.Elapsed.TotalSeconds:F3} с");
        Console.WriteLine($"Контрольная сумма C: {c.Checksum()}");
        if (n <= 10 && k <= 10)
        {
            Console.WriteLine("Матрица C:");
            c.Print(Console.Out);
        }

        if (args.Has("verify"))
        {
            Matrix reference = TriangularMultiplier.Multiply(a, b, 1, Distribution.Static);
            Console.WriteLine(reference.ContentEquals(c)
                ? "Проверка: результат совпадает с последовательным (1 поток) — OK"
                : "Проверка: РЕЗУЛЬТАТ НЕ СОВПАДАЕТ с последовательным!");
        }
        return 0;
    }

    private static void RequirePositive(int value, string name)
    {
        if (value < 1) throw new ArgumentException($"{name} должно быть >= 1");
    }

    private static LoopOrder ParseOrder(string text)
    {
        string s = text.ToLowerInvariant();
        if (s == "ijk") return LoopOrder.IJK;
        if (s == "ikj") return LoopOrder.IKJ;
        throw new ArgumentException("--order: ijk или ikj");
    }
}
