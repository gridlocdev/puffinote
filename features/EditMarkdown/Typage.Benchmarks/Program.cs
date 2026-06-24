using BenchmarkDotNet.Running;

// Runs every *Benchmarks class in the assembly. These track the PRD §3 targets for
// caret/scroll latency and the incremental tokenizer. Run with:
//   dotnet run -c Release --project Typage.Benchmarks
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

internal sealed partial class Program;
