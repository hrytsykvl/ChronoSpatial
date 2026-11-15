using System.Runtime.InteropServices.JavaScript;

namespace SpatialEngine.Web.Interop;

/// <summary>
/// Typed bindings to the <c>renderer</c> JavaScript module.
/// </summary>
/// <remarks>
/// Every buffer crosses as a <see cref="JSType.MemoryView"/>, which hands JavaScript a view directly over
/// the WebAssembly linear heap for the duration of the call. Nothing is serialised: no JSON, no per-element
/// marshalling, no intermediate arrays. That is what makes a 100k-agent frame affordable, because the
/// per-frame interop cost becomes independent of the agent count.
/// <para>
/// JavaScript calls back into .NET through delegates marshalled as <see cref="JSType.Function"/> rather than
/// by resolving exports at runtime. The signatures are then checked by the compiler, and the module needs no
/// knowledge of assembly names.
/// </para>
/// </remarks>
internal static partial class CanvasInterop
{
    internal const string ModuleName = "renderer";

    /// <summary>Loads the renderer module. Must complete before any other method here is called.</summary>
    internal static Task ImportAsync()
        => JSHost.ImportAsync(ModuleName, "../js/renderer.js");

    /// <summary>Binds the canvases and wires pointer input back into .NET.</summary>
    /// <remarks>
    /// Pointer position and activation are separate callbacks because source-generated JavaScript interop
    /// currently marshals at most three arguments per delegate.
    /// </remarks>
    [JSImport("initialize", ModuleName)]
    internal static partial void Initialize(
        int width,
        int height,
        [JSMarshalAs<JSType.Function<JSType.Number, JSType.Number, JSType.Number>>]
        Action<double, double, double> onPointerMove,
        [JSMarshalAs<JSType.Function<JSType.Boolean>>]
        Action<bool> onPointerActive,
        [JSMarshalAs<JSType.Function<JSType.Number, JSType.Number>>]
        Action<double, double> onResize);

    /// <summary>Starts the <c>requestAnimationFrame</c> loop, which drives <paramref name="onFrame"/>.</summary>
    [JSImport("start", ModuleName)]
    internal static partial void Start(
        [JSMarshalAs<JSType.Function<JSType.Number>>] Action<double> onFrame);

    [JSImport("stop", ModuleName)]
    internal static partial void Stop();

    /// <summary>
    /// Presents one frame. <paramref name="rects"/> is only read when <paramref name="rectCount"/> is
    /// positive, so an unchanged wireframe costs nothing beyond the argument.
    /// </summary>
    [JSImport("renderFrame", ModuleName)]
    internal static partial void RenderFrame(
        [JSMarshalAs<JSType.MemoryView>] ArraySegment<byte> rgba,
        [JSMarshalAs<JSType.MemoryView>] ArraySegment<byte> rects,
        int rectCount,
        bool wireframeChanged,
        bool showGrid,
        [JSMarshalAs<JSType.MemoryView>] ArraySegment<byte> stats);
}
