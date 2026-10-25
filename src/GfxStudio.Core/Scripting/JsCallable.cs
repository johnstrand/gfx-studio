using GfxStudio.Core.Diagnostics;
using GfxStudio.Core.Evaluation;
using GfxStudio.Core.Primitives;

namespace GfxStudio.Core.Scripting;

/// <summary>Exposes an exported JavaScript function as an <see cref="IGfxCallable"/>.</summary>
public sealed class JsCallable(JsScriptHost host, string functionName) : IGfxCallable
{
    public JsScriptHost Host => host;

    public string Name => functionName;

    public GfxValue Invoke(IReadOnlyList<GfxValue> arguments, EvaluationContext context, SourceSpan callSpan)
    {
        return host.InvokeExport(functionName, arguments, context, callSpan);
    }
}
