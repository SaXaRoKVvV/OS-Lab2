using System.Globalization;

namespace MatMul.App;

/// <summary>
/// Очень простой разбор командной строки:
///   позиционные аргументы (числа, слова) и параметры вида "--имя значение" или флаги "--флаг".
/// </summary>
internal sealed class Args
{
    public List<string> Positional { get; } = new List<string>();

    private readonly Dictionary<string, string> _options =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _flags =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public Args(string[] args, params string[] flagNames)
    {
        var flagSet = new HashSet<string>(flagNames, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < args.Length; i++)
        {
            string token = args[i];
            if (token.StartsWith("--"))
            {
                string name = token.Substring(2);
                if (flagSet.Contains(name))
                {
                    _flags.Add(name);
                }
                else
                {
                    if (i + 1 >= args.Length)
                        throw new ArgumentException($"Для параметра --{name} не указано значение");
                    i++;
                    _options[name] = args[i];
                }
            }
            else
            {
                Positional.Add(token);
            }
        }
    }

    public bool Has(string flag) => _flags.Contains(flag);

    public string Get(string name, string defaultValue)
    {
        return _options.TryGetValue(name, out string? value) ? value : defaultValue;
    }

    public int GetInt(string name, int defaultValue)
    {
        return _options.TryGetValue(name, out string? value) ? ParseInt(value, "--" + name) : defaultValue;
    }

    public List<int> GetIntList(string name, IEnumerable<int> defaultValue)
    {
        if (!_options.TryGetValue(name, out string? value))
            return new List<int>(defaultValue);

        var result = new List<int>();
        foreach (string part in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
            result.Add(ParseInt(part.Trim(), "--" + name));
        return result;
    }

    public int PositionalInt(int index, string description)
    {
        if (index >= Positional.Count)
            throw new ArgumentException($"Не хватает аргумента: {description}");
        return ParseInt(Positional[index], description);
    }

    public string PositionalString(int index, string description)
    {
        if (index >= Positional.Count)
            throw new ArgumentException($"Не хватает аргумента: {description}");
        return Positional[index];
    }

    public static int ParseInt(string text, string what)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            throw new ArgumentException($"{what}: '{text}' — не целое число");
        return value;
    }
}
