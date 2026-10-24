using System.Collections.ObjectModel;
using GfxStudio.Core.Evaluation;

namespace GfxStudio.Core.Functions;

/// <summary>Stores functions available during .gfx expression evaluation.</summary>
public sealed class GfxFunctionRegistry
{
    private readonly Dictionary<string, IGfxCallable> _functions = new(StringComparer.Ordinal);
    private readonly IReadOnlyDictionary<string, IGfxCallable> _readOnlyFunctions;

    public GfxFunctionRegistry()
    {
        _readOnlyFunctions = new ReadOnlyDictionary<string, IGfxCallable>(_functions);
        StandardFunctions.RegisterAll(this);
    }

    public IReadOnlyDictionary<string, IGfxCallable> Functions => _readOnlyFunctions;

    public static GfxFunctionRegistry CreateDefault() => new();

    /// <summary>Registers or replaces a callable under its name.</summary>
    public void Register(IGfxCallable function)
    {
        ArgumentNullException.ThrowIfNull(function);
        ArgumentException.ThrowIfNullOrWhiteSpace(function.Name);
        _functions[function.Name] = function;
    }

    public bool TryGetFunction(string name, out IGfxCallable function) =>
        _functions.TryGetValue(name, out function!);
}
