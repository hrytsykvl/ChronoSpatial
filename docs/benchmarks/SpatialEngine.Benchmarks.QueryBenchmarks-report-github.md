```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9974/25H2/2025Update/HudsonValley2)
Intel Core Ultra 7 165H 1.40GHz, 1 CPU, 22 logical and 16 physical cores
.NET SDK 10.0.300
  [Host]   : .NET 10.0.15 (10.0.15, 10.0.1526.53008), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.15 (10.0.15, 10.0.1526.53008), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method             | AgentCount | Layout    | AreaFraction | Mean        | Error        | StdDev     | Ratio | RatioSD | Allocated | Alloc Ratio |
|------------------- |----------- |---------- |------------- |------------:|-------------:|-----------:|------:|--------:|----------:|------------:|
| Flat_Query_Buffer  | 100000     | Uniform   | 0.001        |   158.65 μs |   216.325 μs |  11.858 μs |  0.12 |    0.02 |         - |          NA |
| Flat_Query_Visitor | 100000     | Uniform   | 0.001        |   165.27 μs |   535.306 μs |  29.342 μs |  0.13 |    0.02 |         - |          NA |
| Oop_Query          | 100000     | Uniform   | 0.001        |   172.74 μs |   199.501 μs |  10.935 μs |  0.13 |    0.02 |         - |          NA |
| Brute_Query        | 100000     | Uniform   | 0.001        | 1,312.19 μs | 2,987.306 μs | 163.744 μs |  1.01 |    0.16 |         - |          NA |
|                    |            |           |              |             |              |            |       |         |           |             |
| Oop_Query          | 100000     | Uniform   | 0.01         |   897.24 μs |   988.923 μs |  54.206 μs |  0.49 |    0.03 |         - |          NA |
| Flat_Query_Visitor | 100000     | Uniform   | 0.01         | 1,138.00 μs | 1,273.643 μs |  69.813 μs |  0.62 |    0.03 |         - |          NA |
| Flat_Query_Buffer  | 100000     | Uniform   | 0.01         | 1,243.26 μs | 3,110.658 μs | 170.506 μs |  0.68 |    0.08 |         - |          NA |
| Brute_Query        | 100000     | Uniform   | 0.01         | 1,826.92 μs |   563.385 μs |  30.881 μs |  1.00 |    0.02 |         - |          NA |
|                    |            |           |              |             |              |            |       |         |           |             |
| Oop_Query          | 100000     | Uniform   | 0.05         | 2,963.59 μs |   995.350 μs |  54.559 μs |  0.60 |    0.02 |         - |          NA |
| Flat_Query_Buffer  | 100000     | Uniform   | 0.05         | 4,129.66 μs | 3,184.522 μs | 174.554 μs |  0.84 |    0.04 |         - |          NA |
| Flat_Query_Visitor | 100000     | Uniform   | 0.05         | 4,409.49 μs | 5,233.052 μs | 286.841 μs |  0.89 |    0.06 |         - |          NA |
| Brute_Query        | 100000     | Uniform   | 0.05         | 4,934.64 μs | 3,478.180 μs | 190.651 μs |  1.00 |    0.05 |         - |          NA |
|                    |            |           |              |             |              |            |       |         |           |             |
| Oop_Query          | 100000     | Clustered | 0.001        |    10.09 μs |    15.513 μs |   0.850 μs |  0.01 |    0.00 |         - |          NA |
| Flat_Query_Buffer  | 100000     | Clustered | 0.001        |    11.61 μs |     5.057 μs |   0.277 μs |  0.02 |    0.00 |         - |          NA |
| Flat_Query_Visitor | 100000     | Clustered | 0.001        |    12.27 μs |    21.118 μs |   1.158 μs |  0.02 |    0.00 |         - |          NA |
| Brute_Query        | 100000     | Clustered | 0.001        |   770.52 μs |   239.188 μs |  13.111 μs |  1.00 |    0.02 |         - |          NA |
|                    |            |           |              |             |              |            |       |         |           |             |
| Oop_Query          | 100000     | Clustered | 0.01         |   160.51 μs |    31.937 μs |   1.751 μs |  0.12 |    0.01 |         - |          NA |
| Flat_Query_Buffer  | 100000     | Clustered | 0.01         |   358.62 μs |   216.291 μs |  11.856 μs |  0.27 |    0.02 |         - |          NA |
| Flat_Query_Visitor | 100000     | Clustered | 0.01         |   363.84 μs |   457.869 μs |  25.097 μs |  0.28 |    0.02 |         - |          NA |
| Brute_Query        | 100000     | Clustered | 0.01         | 1,312.49 μs | 1,459.635 μs |  80.008 μs |  1.00 |    0.08 |         - |          NA |
|                    |            |           |              |             |              |            |       |         |           |             |
| Oop_Query          | 100000     | Clustered | 0.05         | 1,008.92 μs |   453.758 μs |  24.872 μs |  0.26 |    0.01 |         - |          NA |
| Flat_Query_Buffer  | 100000     | Clustered | 0.05         | 2,463.28 μs | 2,448.464 μs | 134.209 μs |  0.64 |    0.03 |         - |          NA |
| Flat_Query_Visitor | 100000     | Clustered | 0.05         | 2,620.77 μs | 2,427.421 μs | 133.055 μs |  0.68 |    0.03 |         - |          NA |
| Brute_Query        | 100000     | Clustered | 0.05         | 3,839.31 μs | 1,194.797 μs |  65.491 μs |  1.00 |    0.02 |         - |          NA |
