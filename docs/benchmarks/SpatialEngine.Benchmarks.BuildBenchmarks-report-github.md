```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9974/25H2/2025Update/HudsonValley2)
Intel Core Ultra 7 165H 1.40GHz, 1 CPU, 22 logical and 16 physical cores
.NET SDK 10.0.300
  [Host] : .NET 10.0.15 (10.0.15, 10.0.1526.53008), X64 RyuJIT x86-64-v3
  Dry    : .NET 10.0.15 (10.0.15, 10.0.1526.53008), X64 RyuJIT x86-64-v3

Job=Dry  IterationCount=1  LaunchCount=1  
RunStrategy=ColdStart  UnrollFactor=1  WarmupCount=1  

```
| Method      | AgentCount | Layout    | Mean        | Error | Ratio | Allocated  | Alloc Ratio |
|------------ |----------- |---------- |------------:|------:|------:|-----------:|------------:|
| Brute_Build | 10000      | Uniform   |    838.9 μs |    NA |  1.00 |          - |          NA |
| Oop_Build   | 10000      | Uniform   |  9,676.1 μs |    NA | 11.53 |   959176 B |          NA |
| Flat_Build  | 10000      | Uniform   | 14,510.6 μs |    NA | 17.30 |          - |          NA |
|             |            |           |             |       |       |            |             |
| Brute_Build | 10000      | Clustered |  1,054.8 μs |    NA |  1.00 |          - |          NA |
| Oop_Build   | 10000      | Clustered | 11,890.8 μs |    NA | 11.27 |   847360 B |          NA |
| Flat_Build  | 10000      | Clustered | 21,661.9 μs |    NA | 20.54 |          - |          NA |
|             |            |           |             |       |       |            |             |
| Brute_Build | 100000     | Uniform   |  1,092.6 μs |    NA |  1.00 |          - |          NA |
| Flat_Build  | 100000     | Uniform   | 45,043.1 μs |    NA | 41.23 |          - |          NA |
| Oop_Build   | 100000     | Uniform   | 67,724.0 μs |    NA | 61.98 | 11311224 B |          NA |
|             |            |           |             |       |       |            |             |
| Brute_Build | 100000     | Clustered |  1,184.7 μs |    NA |  1.00 |          - |          NA |
| Oop_Build   | 100000     | Clustered | 40,705.8 μs |    NA | 34.36 |  6374640 B |          NA |
| Flat_Build  | 100000     | Clustered | 44,274.4 μs |    NA | 37.37 |          - |          NA |
