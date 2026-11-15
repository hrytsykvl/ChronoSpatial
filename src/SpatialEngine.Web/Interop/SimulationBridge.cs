using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using SpatialEngine.Core.Flat;
using SpatialEngine.Core.Primitives;
using SpatialEngine.Simulation;
using SpatialEngine.Web.Rendering;

namespace SpatialEngine.Web.Interop;

/// <summary>
/// The frame loop. JavaScript owns <c>requestAnimationFrame</c> and calls <see cref="OnFrame"/>; this class
/// advances the simulation, rasterises it and hands the buffers straight back.
/// </summary>
/// <remarks>
/// The Blazor render tree is deliberately not part of the animation loop. Components only read
/// <see cref="Snapshot"/> a few times a second and write configuration, so a 60 FPS frame never triggers a
/// diff or a re-render.
/// <para>
/// The <c>[JSExport]</c> entry points mirror the delegate-based callbacks and exist so the simulator can
/// also be driven from outside .NET (the browser console, or an automated capture script).
/// </para>
/// </remarks>
internal static partial class SimulationBridge
{
    /// <summary>Stat slots handed to JavaScript as float32.</summary>
    private const int StatCount = 12;

    private static readonly float[] Stats = new float[StatCount];
    private static readonly byte[] StatBytes = new byte[StatCount * sizeof(float)];

    private static SimulationHost? _host;
    private static FrameBuffer? _frame;
    private static WireframeBuffer? _wireframe;
    private static int[] _queryResults = [];

    private static Circle _probe;
    private static bool _probeActive;
    private static bool _showGrid = true;
    private static bool _gridLeavesOnly;
    private static long _lastFrameTimestamp;
    private static double _smoothedFps;
    private static double _smoothedMoveUs;
    private static double _smoothedUpdateUs;
    private static double _smoothedTickUs;
    private static double _smoothedQueryUs;

    /// <summary>Square world. A square keeps the quadtree's quadrants square, which keeps splits uniform.</summary>
    public static readonly Aabb World = new(0f, 0f, 2048f, 2048f);

    public static bool IsRunning => _host is not null;

    /// <summary>Latest telemetry, for the UI to poll at a low rate.</summary>
    public static FrameTelemetry Snapshot { get; private set; }

    public static double Fps => _smoothedFps;

    public static int AgentCount => _host?.AgentCount ?? 0;

    public static bool Paused => _host?.Paused ?? true;

    public static bool ShowGrid => _showGrid;

    public static int MaxDepth => _host?.Options.MaxDepth ?? 0;

    public static int NodeCapacity => _host?.Options.NodeCapacity ?? 0;

    public static float ProbeRadiusWorld => _probe.Radius;

    public static async Task StartAsync(int width, int height, int agentCount)
    {
        await CanvasInterop.ImportAsync();

        _host = new SimulationHost(World, agentCount, QuadtreeOptions.Default(World) with
        {
            MaxDepth = 8,
            NodeCapacity = 16,
            InitialCapacity = Math.Max(agentCount, 100_000),
        });

        _frame = new FrameBuffer(World, width, height);
        _wireframe = new WireframeBuffer();
        _queryResults = new int[Math.Max(agentCount, 1)];
        _probe = new Circle(World.CenterX, World.CenterY, World.Width * 0.06f);

        CanvasInterop.Initialize(width, height, OnPointerMove, OnPointerActive, OnResize);
        _lastFrameTimestamp = Stopwatch.GetTimestamp();
        CanvasInterop.Start(OnFrame);
    }

    /// <summary>Called once per animation frame from JavaScript with the delta in milliseconds.</summary>
    private static void OnFrame(double deltaMilliseconds)
    {
        if (_host is null || _frame is null || _wireframe is null)
        {
            return;
        }

        long now = Stopwatch.GetTimestamp();
        double wallSeconds = (now - _lastFrameTimestamp) / (double)Stopwatch.Frequency;
        _lastFrameTimestamp = now;
        if (wallSeconds > 0.0)
        {
            // Exponential smoothing: a raw per-frame FPS reading is too noisy to read off the screen.
            double instant = 1.0 / wallSeconds;
            _smoothedFps = _smoothedFps <= 0.0 ? instant : (_smoothedFps * 0.9) + (instant * 0.1);
        }

        _host.Advance(Math.Min(deltaMilliseconds, 250.0) / 1000.0);

        int hits = 0;
        if (_probeActive)
        {
            hits = _host.QueryCircle(_probe, _queryResults);
        }

        ReadOnlySpan<float> xs = _host.Agents.X;
        ReadOnlySpan<float> ys = _host.Agents.Y;

        _frame.Clear(FrameBuffer.Rgba(12, 14, 20));
        _frame.SplatAll(xs, ys, FrameBuffer.Rgba(90, 190, 255, 230));
        if (hits > 0)
        {
            _frame.SplatSelected(xs, ys, _queryResults, Math.Min(hits, _queryResults.Length), FrameBuffer.Rgba(255, 196, 64));
        }

        bool wireframeChanged = false;
        int rectCount = 0;
        if (_showGrid && _host.Index is FlatQuadtree tree)
        {
            wireframeChanged = _wireframe.Refresh(tree, _frame.Width, _frame.Height, _gridLeavesOnly);
            rectCount = _wireframe.RectCount;
        }

        Snapshot = _host.Telemetry;
        PackStats(hits);

        CanvasInterop.RenderFrame(
            _frame.Pixels,
            _wireframe.Rects,
            rectCount,
            wireframeChanged,
            _showGrid,
            new ArraySegment<byte>(StatBytes));
    }

    private static void PackStats(int hits)
    {
        FrameTelemetry t = Snapshot;

        // Browsers clamp their high-resolution clock (100 us in Chrome without cross-origin isolation),
        // so a single sub-100-us measurement reads as either 0 or 100. Smoothing across frames recovers
        // a meaningful average from those quantised samples.
        _smoothedMoveUs = Smooth(_smoothedMoveUs, t.MoveMicroseconds);
        _smoothedUpdateUs = Smooth(_smoothedUpdateUs, t.UpdateMicroseconds);
        _smoothedTickUs = Smooth(_smoothedTickUs, t.TickMicroseconds);
        if (_probeActive)
        {
            _smoothedQueryUs = Smooth(_smoothedQueryUs, t.QueryMicroseconds);
        }

        Stats[0] = (float)_smoothedFps;
        Stats[1] = (float)_smoothedMoveUs;
        Stats[2] = (float)_smoothedUpdateUs;
        Stats[3] = (float)_smoothedQueryUs;
        Stats[4] = (float)_smoothedTickUs;
        Stats[5] = t.AgentCount;
        Stats[6] = t.NodeCount;
        Stats[7] = t.TreeDepth;
        Stats[8] = hits;
        Stats[9] = (float)t.DirtyCheckHitRate;
        Stats[10] = _probeActive ? 1f : 0f;
        Stats[11] = _probe.Radius;

        MemoryMarshal.AsBytes(Stats.AsSpan()).CopyTo(StatBytes);
    }

    private static double Smooth(double previous, double sample)
        => previous <= 0.0 ? sample : (previous * 0.92) + (sample * 0.08);

    /// <summary>Pointer position in canvas pixels; converted to world units here.</summary>
    private static void OnPointerMove(double canvasX, double canvasY, double radiusPixels)
    {
        if (_frame is null)
        {
            return;
        }

        float worldX = World.MinX + ((float)canvasX / _frame.Width * World.Width);
        float worldY = World.MinY + ((float)canvasY / _frame.Height * World.Height);
        float worldRadius = (float)radiusPixels / _frame.Width * World.Width;

        _probe = new Circle(worldX, worldY, MathF.Max(worldRadius, 1f));
    }

    private static void OnPointerActive(bool active) => _probeActive = active;

    private static void OnResize(double width, double height)
    {
        if (_frame is null || _wireframe is null)
        {
            return;
        }

        _frame.Resize((int)width, (int)height);
        _wireframe.Invalidate();
    }

    // ---- control surface, called from Blazor components and exported to JavaScript ----

    [JSExport]
    internal static void SetPaused(bool paused)
    {
        if (_host is not null)
        {
            _host.Paused = paused;
        }
    }

    [JSExport]
    internal static void SetShowGrid(bool show) => _showGrid = show;

    [JSExport]
    internal static void SetGridLeavesOnly(bool leavesOnly)
    {
        _gridLeavesOnly = leavesOnly;
        _wireframe?.Invalidate();
    }

    [JSExport]
    internal static void SetAgentCount(int count)
    {
        if (_host is null)
        {
            return;
        }

        count = Math.Clamp(count, 1, 250_000);
        _host.SetAgentCount(count);
        if (_queryResults.Length < count)
        {
            _queryResults = new int[count];
        }

        ResetSmoothedTimings();
        _wireframe?.Invalidate();
    }

    [JSExport]
    internal static void SetTreeParams(int maxDepth, int nodeCapacity)
    {
        _host?.SetTreeParams(Math.Clamp(maxDepth, 1, 12), Math.Clamp(nodeCapacity, 1, 256));
        ResetSmoothedTimings();
        _wireframe?.Invalidate();
    }

    /// <summary>Drops smoothed timings so the panel converges on the new workload instead of fading into it.</summary>
    private static void ResetSmoothedTimings()
    {
        _smoothedMoveUs = 0.0;
        _smoothedUpdateUs = 0.0;
        _smoothedTickUs = 0.0;
        _smoothedQueryUs = 0.0;
    }

    [JSExport]
    internal static void SetTickRate(double ticksPerSecond)
    {
        if (_host is not null)
        {
            _host.TickRate = Math.Clamp(ticksPerSecond, 1.0, 240.0);
        }
    }

    [JSExport]
    internal static void TriggerHotspot(double x, double y, double strength)
        => _host?.TriggerHotspot((float)x, (float)y, (float)strength, radius: World.Width * 0.75f, timeToLive: 3.5f);

    /// <summary>Drops a hotspot at the current probe, or the world centre when the probe is inactive.</summary>
    internal static void TriggerHotspotAtProbe(float strength = 2600f)
    {
        float x = _probeActive ? _probe.X : World.CenterX;
        float y = _probeActive ? _probe.Y : World.CenterY;
        TriggerHotspot(x, y, strength);
    }

    [JSExport]
    internal static void ClearHotspots() => _host?.Gravity.Clear();

    [JSExport]
    internal static void SetPointer(double canvasX, double canvasY, double radiusPixels, bool active)
    {
        OnPointerMove(canvasX, canvasY, radiusPixels);
        OnPointerActive(active);
    }

    [JSExport]
    internal static void Resize(int width, int height) => OnResize(width, height);
}
