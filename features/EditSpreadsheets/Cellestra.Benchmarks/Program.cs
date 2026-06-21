using BenchmarkDotNet.Running;

// Runs every *Benchmarks class in the assembly. Benchmarks enforce the
// PRD §3 speed targets (open, scroll, recalc). Run with:
//   dotnet run -c Release --project tests/Cellestra.Benchmarks
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

internal sealed partial class Program;
