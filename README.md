# Многопоточное умножение матриц (C#, .NET 8)

Лабораторная работа по ОС. Умножение матриц вручную созданными потоками, прогресс, генерация матриц в отдельном процессе (IPC через канал), нижнетреугольная матрица со статическим и динамическим распределением строк.

## Сборка и тесты

```
dotnet build src/MatMul.App -c Release
dotnet test tests/MatMul.Tests -c Release
```

## Запуск

Исполняемый файл: `src/MatMul.App/bin/Release/net8.0/matmul.exe`

```
matmul mul <n> <m> <k> <T> [--ipc] [--verify]        обычное умножение, T потоков
matmul tri <n> <k> <T> static|dynamic [--chunk C]     треугольная матрица A
matmul bench-mul | bench-order | bench-tri | bench-ipc   серии замеров (CSV)
```

Примеры: `matmul mul 1000 1000 1000 8`, `matmul tri 2000 2000 8 dynamic --ipc`.

Результаты замеров (сырые и усреднённые) лежат в папке `results/`.
