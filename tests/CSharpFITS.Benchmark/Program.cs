using BenchmarkDotNet.Running;
using CSharpFITS.Benchmark;

if (args.Length > 0 && args[0] == "quick")
{
    QuickBaseline.Run();
}
else
{
    BenchmarkRunner.Run<FitsLoadBenchmark>();
}
