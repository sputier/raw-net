using BenchmarkDotNet.Running;

// Every benchmark class of this assembly can be run by name:
//   dotnet run -c Release --project benchmarks/Eleris.RawNet.Benchmarks -- --filter "*Metadata*"
// The benchmarks are added with the feature they measure (see CONTRIBUTING.md).
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
