using BenchmarkDotNet.Running;

// Runs every *Benchmarks class in the assembly. These measure the 50 MB load path proposal 0003 §9.5
// says to measure before optimising. Run with:
//   dotnet run -c Release --project features/EditPlaintext/Plaintain.Benchmarks
//   just bench-plaintain
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

internal sealed partial class Program;
