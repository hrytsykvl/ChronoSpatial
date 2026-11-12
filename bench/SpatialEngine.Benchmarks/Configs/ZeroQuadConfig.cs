using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Csv;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Order;

namespace SpatialEngine.Benchmarks.Configs;

/// <summary>
/// Shared BenchmarkDotNet configuration.
/// </summary>
/// <remarks>
/// <see cref="MemoryDiagnoser"/> is the point of the whole suite: the headline claim is that the flat
/// quadtree's tick allocates 0 B while the OOP quadtree allocates per tick and triggers Gen0 collections.
/// Hardware counters need an elevated process and ETW, so they are attached only when both are available;
/// otherwise the run would fail outright instead of simply reporting less.
/// </remarks>
public sealed class ZeroQuadConfig : ManualConfig
{
    public ZeroQuadConfig()
    {
        AddDiagnoser(MemoryDiagnoser.Default);
        AddExporter(MarkdownExporter.GitHub, CsvExporter.Default, HtmlExporter.Default);
        AddLogger(ConsoleLogger.Default);
        AddColumnProvider(DefaultColumnProviders.Instance);
        WithOrderer(new DefaultOrderer(SummaryOrderPolicy.FastestToSlowest));
        WithOptions(ConfigOptions.JoinSummary | ConfigOptions.DisableLogFile);

        TryAddHardwareCounters();
    }

    private void TryAddHardwareCounters()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            using System.Security.Principal.WindowsIdentity identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            System.Security.Principal.WindowsPrincipal principal = new(identity);
            if (!principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))
            {
                ConsoleLogger.Default.WriteLineHint(
                    "Hardware counters skipped: run elevated to collect cache misses and branch mispredictions.");
                return;
            }

            AddDiagnoser(new BenchmarkDotNet.Diagnostics.Windows.EtwProfiler());
            AddHardwareCounters(HardwareCounter.CacheMisses, HardwareCounter.BranchMispredictions);
        }
        catch (PlatformNotSupportedException)
        {
            // No ETW on this host; the rest of the suite is still meaningful.
        }
    }
}
