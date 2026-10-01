using BenchmarkDotNet.Running;
using ThrottledLogging.Benchmarks;

BenchmarkRunner.Run<LogPathBenchmarks>(args: args);
