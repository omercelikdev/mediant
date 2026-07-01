/**
 * Benchmark data from BenchmarkDotNet runs.
 * Values in nanoseconds (ns) for send/publish, bytes (B) for memory.
 * Update these after running: cd tests/Mediant.Benchmarks && dotnet run -c Release
 */

export const sendBenchmarks = [
  { name: "0 Behaviors", mediant: 25, mediatr: 24 },
  { name: "1 Behavior", mediant: 64, mediatr: 58 },
  { name: "2 Behaviors", mediant: 74, mediatr: 74 },
  { name: "4 Behaviors", mediant: 102, mediatr: 104 },
  { name: "8 Behaviors", mediant: 160, mediatr: 164 },
  { name: "16 Behaviors", mediant: 270, mediatr: 280 },
  { name: "32 Behaviors", mediant: 490, mediatr: 516 },
];

export const publishBenchmarks = [
  { name: "1 Handler", mediant: 23, mediatr: 44 },
  { name: "10 Handlers", mediant: 69, mediatr: 185 },
  { name: "50 Handlers", mediant: 273, mediatr: 786 },
  { name: "100 Handlers", mediant: 530, mediatr: 1608 },
];

export const memoryBenchmarks = [
  { name: "Send (0 beh)", mediant: 64, mediatr: 128 },
  { name: "Send (4 beh)", mediant: 696, mediatr: 800 },
  { name: "Query", mediant: 104, mediatr: 200 },
  { name: "Publish (1H)", mediant: 88, mediatr: 288 },
  { name: "Publish (10H)", mediant: 376, mediatr: 1656 },
  { name: "Publish (100H)", mediant: 3256, mediatr: 15336 },
];
