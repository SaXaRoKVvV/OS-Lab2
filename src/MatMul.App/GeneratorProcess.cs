using System.Diagnostics;
using System.Reflection;
using MatMul.Core;

namespace MatMul.App;

/// <summary>
/// Сторона ДОЧЕРНЕГО процесса-генератора. Запускается как:
///   matmul gen dense n m k seed     или     matmul gen tri n k seed
/// Генерирует матрицы и пишет их в стандартный вывод (stdout) двоичным форматом MatrixIo.
/// Родитель при запуске перенаправил этот stdout в анонимный канал (pipe), из которого сам читает.
/// Диагностику нужно писать ТОЛЬКО в stderr — stdout занят данными.
/// </summary>
internal static class GeneratorMode
{
    public static int Run(string[] argv)
    {
        if (argv.Length < 2)
        {
            Console.Error.WriteLine("gen: не указан тип (dense|tri)");
            return 2;
        }

        string kind = argv[1].ToLowerInvariant();
        var sw = Stopwatch.StartNew();

        if (kind == "dense" && argv.Length == 6)
        {
            int n = Args.ParseInt(argv[2], "n");
            int m = Args.ParseInt(argv[3], "m");
            int k = Args.ParseInt(argv[4], "k");
            int seed = Args.ParseInt(argv[5], "seed");

            var rng = new Random(seed);
            Matrix a = Matrix.Generate(n, m, rng);
            Matrix b = Matrix.Generate(m, k, rng);
            long micros = ElapsedMicros(sw);

            using Stream stdout = Console.OpenStandardOutput();
            MatrixIo.WriteDensePair(stdout, a, b, micros);
            stdout.Flush();
            return 0;
        }

        if (kind == "tri" && argv.Length == 5)
        {
            int n = Args.ParseInt(argv[2], "n");
            int k = Args.ParseInt(argv[3], "k");
            int seed = Args.ParseInt(argv[4], "seed");

            var rng = new Random(seed);
            TriangularMatrix a = TriangularMatrix.Generate(n, rng);
            Matrix b = Matrix.Generate(n, k, rng);
            long micros = ElapsedMicros(sw);

            using Stream stdout = Console.OpenStandardOutput();
            MatrixIo.WriteTriangularPair(stdout, a, b, micros);
            stdout.Flush();
            return 0;
        }

        Console.Error.WriteLine("gen: неверные аргументы");
        return 2;
    }

    private static long ElapsedMicros(Stopwatch sw)
    {
        return (long)(sw.Elapsed.TotalMilliseconds * 1000.0);
    }
}

/// <summary>
/// Сторона РОДИТЕЛЯ (вычислителя): запускает генератор как отдельный процесс ОС
/// и читает матрицы из его stdout.
/// </summary>
internal static class GeneratorClient
{
    public static (Matrix A, Matrix B, double GenerationSeconds, double TotalSeconds) ReceiveDense(
        int n, int m, int k, int seed)
    {
        var sw = Stopwatch.StartNew();
        using Process process = Start(new[] { "gen", "dense", n.ToString(), m.ToString(), k.ToString(), seed.ToString() });

        var data = MatrixIo.ReadDensePair(process.StandardOutput.BaseStream);
        process.WaitForExit();
        sw.Stop();

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Процесс-генератор завершился с кодом {process.ExitCode}");
        return (data.A, data.B, data.GenerationMicros / 1e6, sw.Elapsed.TotalSeconds);
    }

    public static (TriangularMatrix A, Matrix B, double GenerationSeconds, double TotalSeconds) ReceiveTriangular(
        int n, int k, int seed)
    {
        var sw = Stopwatch.StartNew();
        using Process process = Start(new[] { "gen", "tri", n.ToString(), k.ToString(), seed.ToString() });

        var data = MatrixIo.ReadTriangularPair(process.StandardOutput.BaseStream);
        process.WaitForExit();
        sw.Stop();

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Процесс-генератор завершился с кодом {process.ExitCode}");
        return (data.A, data.B, data.GenerationMicros / 1e6, sw.Elapsed.TotalSeconds);
    }

    /// <summary>Запускает ту же самую программу в режиме "gen" с перенаправленным stdout.</summary>
    private static Process Start(IEnumerable<string> generatorArgs)
    {
        string exe = Environment.ProcessPath
            ?? throw new InvalidOperationException("Не удалось определить путь к исполняемому файлу");

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            RedirectStandardOutput = true,   // stdout ребёнка = канал, который читаем мы
            CreateNoWindow = true
        };

        // Если запущено через "dotnet matmul.dll" / "dotnet run", exe — это сам dotnet,
        // и первым аргументом нужно передать путь к dll.
        if (Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            psi.ArgumentList.Add(Assembly.GetEntryAssembly()!.Location);

        foreach (string a in generatorArgs)
            psi.ArgumentList.Add(a);

        return Process.Start(psi)
            ?? throw new InvalidOperationException("Не удалось запустить процесс-генератор");
    }
}
