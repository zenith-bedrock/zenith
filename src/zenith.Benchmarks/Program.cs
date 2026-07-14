using BenchmarkDotNet.Running;
using Zenith.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(BinaryStreamBenchmarks).Assembly).Run(args);
