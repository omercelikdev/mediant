/**
 * Benchmark data from BenchmarkDotNet runs.
 * Values in nanoseconds (ns) for send/publish, bytes (B) for memory.
 * Update these after running: cd tests/Mediant.Benchmarks && dotnet run -c Release
 */

export const sendBenchmarks = [
  { name: "0 Behaviors", qorpe: 25, mediatr: 24 },
  { name: "1 Behavior", qorpe: 64, mediatr: 58 },
  { name: "2 Behaviors", qorpe: 74, mediatr: 74 },
  { name: "4 Behaviors", qorpe: 102, mediatr: 104 },
  { name: "8 Behaviors", qorpe: 160, mediatr: 164 },
  { name: "16 Behaviors", qorpe: 270, mediatr: 280 },
  { name: "32 Behaviors", qorpe: 490, mediatr: 516 },
];

export const publishBenchmarks = [
  { name: "1 Handler", qorpe: 23, mediatr: 44 },
  { name: "10 Handlers", qorpe: 69, mediatr: 185 },
  { name: "50 Handlers", qorpe: 273, mediatr: 786 },
  { name: "100 Handlers", qorpe: 530, mediatr: 1608 },
];

export const memoryBenchmarks = [
  { name: "Send (0 beh)", qorpe: 64, mediatr: 128 },
  { name: "Send (4 beh)", qorpe: 696, mediatr: 800 },
  { name: "Query", qorpe: 104, mediatr: 200 },
  { name: "Publish (1H)", qorpe: 88, mediatr: 288 },
  { name: "Publish (10H)", qorpe: 376, mediatr: 1656 },
  { name: "Publish (100H)", qorpe: 3256, mediatr: 15336 },
];
