using BenchmarkDotNet.Running;
using Open3SDCMNet.Benchmarks;

// The generated benchmark process runs from its own build directory.
Environment.SetEnvironmentVariable(BenchmarkInputs.DirectoryVariable, BenchmarkInputs.GetDirectory());
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
