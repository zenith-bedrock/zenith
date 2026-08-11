using BenchmarkDotNet.Running;
using Zenith.Benchmarks;

if (args.Length > 0 && args[0] == "--runtime-load")
    Environment.ExitCode = RuntimeLoadHarness.Run(args[1..]);
else
    BenchmarkSwitcher.FromAssembly(typeof(BinaryStreamBenchmarks).Assembly).Run(args);
