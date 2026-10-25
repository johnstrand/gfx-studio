using GfxStudio.Core.Parsing.Ast;
using GfxStudio.Core.Functions;
using GfxStudio.Core.Primitives;
using GfxStudio.Core.Randomness;

namespace GfxStudio.Core.Evaluation;

/// <summary>Execution state for the current batch run (<c>run.*</c>).</summary>
public readonly record struct RunContext(int Number = 0, int Count = 1, ulong Seed = 0)
{
    public GfxObject ToGfxObject()
    {
        var number = new GfxNumber(Number);
        var props = new Dictionary<string, GfxValue>(StringComparer.Ordinal)
        {
            ["number"] = number,
            ["index"] = number,
            ["count"] = new GfxNumber(Count),
            ["seed"] = new GfxNumber(Seed)
        };
        return new GfxObject(props, ["number", "index", "count", "seed"]);
    }
}

/// <summary>Execution state for the current animation frame (<c>frame.*</c>).</summary>
public readonly record struct FrameContext(int Number = 0, int Count = 1, double Fps = 15.0)
{
    public double Time => Fps > 0.0 ? Number / Fps : 0.0;

    public double Progress => Number / (double)Math.Max(1, Count);

    public GfxObject ToGfxObject()
    {
        var number = new GfxNumber(Number);
        var progress = new GfxNumber(Progress);
        var props = new Dictionary<string, GfxValue>(StringComparer.Ordinal)
        {
            ["number"] = number,
            ["index"] = number,
            ["count"] = new GfxNumber(Count),
            ["time"] = new GfxNumber(Time),
            ["progress"] = progress,
            ["t"] = progress
        };
        return new GfxObject(props, ["number", "index", "count", "time", "progress", "t"]);
    }
}

/// <summary>Provides built-in constant namespaces (<c>NAMED_COLORS</c>, <c>ALIGN</c>) and math constants.</summary>
public static class BuiltInConstants
{
    public static GfxObject NamedColors { get; } = CreateNamedColors();

    public static GfxObject Align { get; } = CreateAlign();

    public static IReadOnlyDictionary<string, GfxValue> RootConstants { get; } = CreateRootConstants();

    private static GfxObject CreateNamedColors()
    {
        var entries = new (string Name, ColorRgba Color)[]
        {
            ("WHITE", new ColorRgba(255, 255, 255, 255)),
            ("BLACK", new ColorRgba(0, 0, 0, 255)),
            ("RED", new ColorRgba(255, 0, 0, 255)),
            ("GREEN", new ColorRgba(0, 255, 0, 255)),
            ("BLUE", new ColorRgba(0, 0, 255, 255)),
            ("CYAN", new ColorRgba(0, 255, 255, 255)),
            ("MAGENTA", new ColorRgba(255, 0, 255, 255)),
            ("YELLOW", new ColorRgba(255, 255, 0, 255)),
            ("TRANSPARENT", new ColorRgba(0, 0, 0, 0)),
            ("GRAY", new ColorRgba(128, 128, 128, 255)),
            ("GREY", new ColorRgba(128, 128, 128, 255)),
            ("ORANGE", new ColorRgba(255, 165, 0, 255)),
            ("PURPLE", new ColorRgba(128, 0, 128, 255)),
            ("PINK", new ColorRgba(255, 192, 203, 255)),
            ("BROWN", new ColorRgba(165, 42, 42, 255))
        };

        var dict = new Dictionary<string, GfxValue>(entries.Length, StringComparer.Ordinal);
        var keys = new List<string>(entries.Length);
        foreach (var (name, color) in entries)
        {
            dict[name] = new GfxColor(color);
            keys.Add(name);
        }

        return new GfxObject(dict, keys.AsReadOnly());
    }

    private static GfxObject CreateAlign()
    {
        string[] names = ["CENTER", "LEFT", "RIGHT", "TOP", "BOTTOM"];
        var dict = new Dictionary<string, GfxValue>(names.Length, StringComparer.Ordinal);
        foreach (var name in names)
        {
            dict[name] = new GfxSymbol(name);
        }

        return new GfxObject(dict, names);
    }

    private static IReadOnlyDictionary<string, GfxValue> CreateRootConstants()
    {
        return new Dictionary<string, GfxValue>(StringComparer.Ordinal)
        {
            ["NAMED_COLORS"] = NamedColors,
            ["ALIGN"] = Align,
            ["PI"] = new GfxNumber(Math.PI),
            ["TAU"] = new GfxNumber(Math.Tau),
            ["E"] = new GfxNumber(Math.E)
        };
    }
}

/// <summary>Holds the state and environment for evaluating a .gfx document during a render pass.</summary>
public sealed class EvaluationContext
{
    private readonly Dictionary<string, GfxValue> _variables = new(StringComparer.Ordinal);

    public EvaluationContext(
        GfxDocument? document = null,
        RunContext run = default,
        FrameContext frame = default,
        IGfxRandom? random = null,
        IEnumerable<IGfxCallable>? functions = null,
        GfxFunctionRegistry? functionRegistry = null,
        IGfxRandom? frameRandom = null)
    {
        Document = document;
        Run = run.Count <= 0 ? new RunContext(0, 1, 0) : run;
        Frame = frame.Count <= 0 ? new FrameContext(0, 1, 15.0) : frame;
        Random = random ?? new GfxRandom(Run.Seed);
        FrameRandom = frameRandom ?? new GfxRandom(Run.Seed ^ unchecked((ulong)(long)Frame.Number * 0x9E3779B97F4A7C15UL));
        FunctionRegistry = functionRegistry ?? GfxFunctionRegistry.CreateDefault();

        if (functions is not null)
        {
            foreach (var fn in functions)
            {
                FunctionRegistry.Register(fn);
            }
        }
    }

    public GfxDocument? Document { get; }

    public RunContext Run { get; }

    public FrameContext Frame { get; }

    public IGfxRandom Random { get; set; }

    public IGfxRandom FrameRandom { get; set; }

    public GfxFunctionRegistry FunctionRegistry { get; }

    public IReadOnlyDictionary<string, IGfxCallable> Functions => FunctionRegistry.Functions;

    public IReadOnlyDictionary<string, GfxValue> Variables => _variables;

    public void RegisterFunction(IGfxCallable callable)
    {
        FunctionRegistry.Register(callable);
    }

    public void SetVariable(string name, GfxValue value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);
        _variables[name] = value;
    }

    public bool TryGetFunction(string name, out IGfxCallable callable) =>
        FunctionRegistry.TryGetFunction(name, out callable!);
}
