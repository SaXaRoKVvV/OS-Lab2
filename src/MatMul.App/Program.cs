using System.Runtime.InteropServices;
using System.Text;

namespace MatMul.App;

internal static class Program
{
    private static int Main(string[] argv)
    {
        if (argv.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        string command = argv[0].ToLowerInvariant();
        try
        {
            // Режим дочернего процесса-генератора: в stdout идут ДВОИЧНЫЕ данные,
            // поэтому здесь нельзя ничего печатать в Console.Out и менять кодировку.
            if (command == "gen")
                return GeneratorMode.Run(argv);

            Console.OutputEncoding = Encoding.UTF8;
            string[] rest = argv.Skip(1).ToArray();

            switch (command)
            {
                case "info":
                    return Info();
                case "mul":
                    return Commands.Mul(new Args(rest, "ipc", "verify", "no-progress"));
                case "tri":
                    return Commands.Tri(new Args(rest, "ipc", "verify", "no-progress"));
                case "bench-mul":
                    return Bench.Mul(new Args(rest));
                case "bench-order":
                    return Bench.Order(new Args(rest));
                case "bench-tri":
                    return Bench.Tri(new Args(rest));
                case "bench-ipc":
                    return Bench.Ipc(new Args(rest));
                default:
                    PrintUsage();
                    return 1;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Ошибка: " + ex.Message);
            return 1;
        }
    }

    private static int Info()
    {
        Console.WriteLine($"Логических процессоров (Environment.ProcessorCount): {Environment.ProcessorCount}");
        Console.WriteLine($"ОС: {RuntimeInformation.OSDescription}");
        Console.WriteLine($"Архитектура процесса: {RuntimeInformation.ProcessArchitecture}");
        Console.WriteLine($"Среда выполнения: {RuntimeInformation.FrameworkDescription}");
        Console.WriteLine("Модель процессора, число физических ядер и размер L3 смотрите в системе");
        Console.WriteLine("(Windows: Диспетчер задач -> Производительность -> ЦП; Linux: lscpu).");
        return 0;
    }

    private static void PrintUsage()
    {
        Console.WriteLine(@"Использование:
  matmul info
  matmul mul <n> <m> <k> <T> [--order ijk|ikj] [--ipc] [--verify] [--no-progress] [--seed S]
        A: n x m, B: m x k, T потоков. --ipc: матрицы генерирует отдельный процесс.
  matmul tri <n> <k> <T> <static|dynamic> [--chunk C] [--ipc] [--verify] [--no-progress] [--seed S]
        A: нижнетреугольная n x n, B: n x k.

Серии замеров (результат — CSV-файлы <out>_raw.csv и <out>_summary.csv):
  matmul bench-mul   [--sizes 500,1000,2000] [--threads 1,2,4,...] [--runs 5] [--out mul]
  matmul bench-order [--n 2000] [--runs 5] [--out order]
  matmul bench-tri   [--sizes 500,1000,2000] [--threads ...] [--chunks 1] [--runs 5] [--out tri]
  matmul bench-ipc   [--sizes 500,1000,2000] [--threads T] [--runs 5] [--out ipc]");
    }
}
