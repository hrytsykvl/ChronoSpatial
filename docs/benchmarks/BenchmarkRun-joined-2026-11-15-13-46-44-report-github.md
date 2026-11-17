```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9974/25H2/2025Update/HudsonValley2)
Intel Core Ultra 7 165H 1.40GHz, 1 CPU, 22 logical and 16 physical cores
.NET SDK 10.0.300
  [Host] : .NET 10.0.15 (10.0.15, 10.0.1526.53008), X64 RyuJIT x86-64-v3
  Dry    : .NET 10.0.15 (10.0.15, 10.0.1526.53008), X64 RyuJIT x86-64-v3

Job=Dry  IterationCount=1  LaunchCount=1  
RunStrategy=ColdStart  UnrollFactor=1  WarmupCount=1  

```
| Type                 | Method             | AgentCount | Layout    | AreaFraction | Mean        | Error | Ratio | Allocated | Alloc Ratio |
|--------------------- |------------------- |----------- |---------- |------------- |------------:|------:|------:|----------:|------------:|
| UpdateTickBenchmarks | Brute_Update       | 10000      | Uniform   | ?            |  4,265.1 μs |    NA |  0.63 |         - |          NA |
| UpdateTickBenchmarks | Flat_FullRebuild   | 10000      | Uniform   | ?            |  5,305.3 μs |    NA |  0.79 |         - |          NA |
| UpdateTickBenchmarks | Move_Only          | 10000      | Uniform   | ?            |  6,751.6 μs |    NA |  1.00 |         - |          NA |
| UpdateTickBenchmarks | Oop_Update         | 10000      | Uniform   | ?            | 13,280.2 μs |    NA |  1.97 |  162736 B |          NA |
| UpdateTickBenchmarks | Flat_DirtyCheck    | 10000      | Uniform   | ?            | 14,957.3 μs |    NA |  2.22 |         - |          NA |
| UpdateTickBenchmarks | Flat_RemoveInsert  | 10000      | Uniform   | ?            | 19,046.8 μs |    NA |  2.82 |         - |          NA |
|                      |                    |            |           |              |             |       |       |           |             |
| UpdateTickBenchmarks | Move_Only          | 10000      | Clustered | ?            |  3,896.3 μs |    NA |  1.00 |         - |          NA |
| UpdateTickBenchmarks | Brute_Update       | 10000      | Clustered | ?            |  4,378.2 μs |    NA |  1.12 |         - |          NA |
| UpdateTickBenchmarks | Flat_FullRebuild   | 10000      | Clustered | ?            |  5,662.3 μs |    NA |  1.45 |         - |          NA |
| UpdateTickBenchmarks | Flat_DirtyCheck    | 10000      | Clustered | ?            | 10,139.4 μs |    NA |  2.60 |         - |          NA |
| UpdateTickBenchmarks | Oop_Update         | 10000      | Clustered | ?            | 13,084.0 μs |    NA |  3.36 |   30904 B |          NA |
| UpdateTickBenchmarks | Flat_RemoveInsert  | 10000      | Clustered | ?            | 16,890.3 μs |    NA |  4.33 |         - |          NA |
|                      |                    |            |           |              |             |       |       |           |             |
| QueryBenchmarks      | Oop_Query          | 100000     | Uniform   | 0.001        |  1,348.2 μs |    NA |  0.35 |         - |          NA |
| QueryBenchmarks      | Flat_Query_Buffer  | 100000     | Uniform   | 0.001        |  2,520.1 μs |    NA |  0.66 |         - |          NA |
| QueryBenchmarks      | Flat_Query_Visitor | 100000     | Uniform   | 0.001        |  2,896.5 μs |    NA |  0.76 |         - |          NA |
| QueryBenchmarks      | Brute_Query        | 100000     | Uniform   | 0.001        |  3,830.9 μs |    NA |  1.00 |         - |          NA |
|                      |                    |            |           |              |             |       |       |           |             |
| QueryBenchmarks      | Brute_Query        | 100000     | Uniform   | 0.01         |  4,643.7 μs |    NA |  1.00 |         - |          NA |
| QueryBenchmarks      | Oop_Query          | 100000     | Uniform   | 0.01         |  7,439.6 μs |    NA |  1.60 |         - |          NA |
| QueryBenchmarks      | Flat_Query_Buffer  | 100000     | Uniform   | 0.01         | 10,333.6 μs |    NA |  2.23 |         - |          NA |
| QueryBenchmarks      | Flat_Query_Visitor | 100000     | Uniform   | 0.01         | 10,693.4 μs |    NA |  2.30 |         - |          NA |
|                      |                    |            |           |              |             |       |       |           |             |
| QueryBenchmarks      | Brute_Query        | 100000     | Uniform   | 0.05         |  7,568.4 μs |    NA |  1.00 |         - |          NA |
| QueryBenchmarks      | Flat_Query_Buffer  | 100000     | Uniform   | 0.05         |  9,788.6 μs |    NA |  1.29 |         - |          NA |
| QueryBenchmarks      | Flat_Query_Visitor | 100000     | Uniform   | 0.05         | 10,061.5 μs |    NA |  1.33 |         - |          NA |
| QueryBenchmarks      | Oop_Query          | 100000     | Uniform   | 0.05         | 17,664.5 μs |    NA |  2.33 |         - |          NA |
|                      |                    |            |           |              |             |       |       |           |             |
| UpdateTickBenchmarks | Brute_Update       | 100000     | Uniform   | ?            | 16,100.4 μs |    NA |  0.85 |         - |          NA |
| UpdateTickBenchmarks | Move_Only          | 100000     | Uniform   | ?            | 18,999.5 μs |    NA |  1.00 |         - |          NA |
| UpdateTickBenchmarks | Flat_DirtyCheck    | 100000     | Uniform   | ?            | 33,624.5 μs |    NA |  1.77 |         - |          NA |
| UpdateTickBenchmarks | Flat_RemoveInsert  | 100000     | Uniform   | ?            | 42,330.7 μs |    NA |  2.23 |         - |          NA |
| UpdateTickBenchmarks | Flat_FullRebuild   | 100000     | Uniform   | ?            | 47,008.8 μs |    NA |  2.47 |         - |          NA |
| UpdateTickBenchmarks | Oop_Update         | 100000     | Uniform   | ?            | 86,140.9 μs |    NA |  4.53 | 1631704 B |          NA |
|                      |                    |            |           |              |             |       |       |           |             |
| QueryBenchmarks      | Oop_Query          | 100000     | Clustered | 0.001        |    811.6 μs |    NA |  0.20 |         - |          NA |
| QueryBenchmarks      | Flat_Query_Buffer  | 100000     | Clustered | 0.001        |    968.9 μs |    NA |  0.24 |         - |          NA |
| QueryBenchmarks      | Flat_Query_Visitor | 100000     | Clustered | 0.001        |    970.9 μs |    NA |  0.24 |         - |          NA |
| QueryBenchmarks      | Brute_Query        | 100000     | Clustered | 0.001        |  4,043.6 μs |    NA |  1.00 |         - |          NA |
|                      |                    |            |           |              |             |       |       |           |             |
| QueryBenchmarks      | Oop_Query          | 100000     | Clustered | 0.01         |  1,977.6 μs |    NA |  0.46 |         - |          NA |
| QueryBenchmarks      | Flat_Query_Visitor | 100000     | Clustered | 0.01         |  4,252.4 μs |    NA |  0.99 |         - |          NA |
| QueryBenchmarks      | Brute_Query        | 100000     | Clustered | 0.01         |  4,286.5 μs |    NA |  1.00 |         - |          NA |
| QueryBenchmarks      | Flat_Query_Buffer  | 100000     | Clustered | 0.01         |  4,444.8 μs |    NA |  1.04 |         - |          NA |
|                      |                    |            |           |              |             |       |       |           |             |
| QueryBenchmarks      | Oop_Query          | 100000     | Clustered | 0.05         |  6,495.5 μs |    NA |  0.83 |         - |          NA |
| QueryBenchmarks      | Flat_Query_Visitor | 100000     | Clustered | 0.05         |  7,197.6 μs |    NA |  0.91 |         - |          NA |
| QueryBenchmarks      | Flat_Query_Buffer  | 100000     | Clustered | 0.05         |  7,705.1 μs |    NA |  0.98 |         - |          NA |
| QueryBenchmarks      | Brute_Query        | 100000     | Clustered | 0.05         |  7,867.0 μs |    NA |  1.00 |         - |          NA |
|                      |                    |            |           |              |             |       |       |           |             |
| UpdateTickBenchmarks | Move_Only          | 100000     | Clustered | ?            | 15,098.2 μs |    NA |  1.00 |         - |          NA |
| UpdateTickBenchmarks | Brute_Update       | 100000     | Clustered | ?            | 16,696.5 μs |    NA |  1.11 |         - |          NA |
| UpdateTickBenchmarks | Flat_DirtyCheck    | 100000     | Clustered | ?            | 26,033.9 μs |    NA |  1.72 |         - |          NA |
| UpdateTickBenchmarks | Flat_RemoveInsert  | 100000     | Clustered | ?            | 30,454.5 μs |    NA |  2.02 |         - |          NA |
| UpdateTickBenchmarks | Flat_FullRebuild   | 100000     | Clustered | ?            | 34,666.3 μs |    NA |  2.30 |         - |          NA |
| UpdateTickBenchmarks | Oop_Update         | 100000     | Clustered | ?            | 65,340.7 μs |    NA |  4.33 |  231560 B |          NA |
