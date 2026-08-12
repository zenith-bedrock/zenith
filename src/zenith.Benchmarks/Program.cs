using BenchmarkDotNet.Running;
using Zenith.Benchmarks;

try
{
    if (args.Length > 0 && args[0] == "--runtime-load")
        Environment.ExitCode = RuntimeLoadHarness.Run(args[1..]);
    else if (args.Length > 0 && args[0] == "--ecs-spike")
        Environment.ExitCode = EcsFeasibilityHarness.Run(args[1..]);
    else
        BenchmarkSwitcher.FromAssembly(typeof(BinaryStreamBenchmarks).Assembly).Run(args);
}
catch (Exception exception)
{
    Console.Error.WriteLine("zenith.Benchmarks failed:");
    Console.Error.WriteLine(exception);
    Environment.ExitCode = 1;
}
