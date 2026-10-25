using System.Text;
using GfxStudio.Core.Diagnostics;
using GfxStudio.Core.Parsing;
using GfxStudio.Core.Parsing.Ast;
using GfxStudio.Core.Primitives;
using GfxStudio.Core.Randomness;

namespace GfxStudio.Core.Evaluation;

/// <summary>Result of evaluating a .gfx document or expression.</summary>
public sealed record EvaluationResult<T>(
    T? Value,
    IReadOnlyList<GfxDiagnostic> Diagnostics)
{
    public bool HasErrors => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
}

/// <summary>
/// Evaluates .gfx AST expressions and documents with lazy cross-reference resolution and cycle detection.
/// </summary>
public sealed class GfxEvaluator
{
    private readonly EvaluationContext _context;
    private readonly DeterministicSeedProvider? _seedProvider;
    private readonly List<GfxDiagnostic> _diagnostics = [];
    private readonly Dictionary<string, PropertyAssignmentNode> _rootAstProperties = new(StringComparer.Ordinal);
    private readonly List<string> _rootOrderedKeys = [];
    private readonly Dictionary<string, GfxValue> _resolvedPathCache = new(StringComparer.Ordinal);
    private readonly List<string> _evaluationStack = [];
    private readonly HashSet<string> _activePaths = new(StringComparer.Ordinal);

    public GfxEvaluator(EvaluationContext context, DeterministicSeedProvider? seedProvider = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        _seedProvider = seedProvider;

        if (context.Document is not null)
        {
            foreach (var prop in context.Document.Properties)
            {
                if (!_rootAstProperties.ContainsKey(prop.Name))
                {
                    _rootOrderedKeys.Add(prop.Name);
                }

                _rootAstProperties[prop.Name] = prop;
            }
        }
    }

    public IReadOnlyList<GfxDiagnostic> Diagnostics => _diagnostics;

    /// <summary>Parses and evaluates the given .gfx source string into a root <see cref="GfxObject"/>.</summary>
    public static EvaluationResult<GfxObject> EvaluateSource(
        string source,
        RunContext run = default,
        FrameContext frame = default,
        IEnumerable<IGfxCallable>? functions = null,
        string filePath = "<memory>")
    {
        var parseResult = GfxParser.Parse(source, filePath);
        if (parseResult.HasErrors || parseResult.Document is null)
        {
            return new EvaluationResult<GfxObject>(null, parseResult.Diagnostics);
        }

        var context = new EvaluationContext(parseResult.Document, run, frame, functions: functions);
        var evaluator = new GfxEvaluator(context);
        return evaluator.EvaluateDocument();
    }

    /// <summary>Evaluates all top-level properties of the document in declaration order.</summary>
    public EvaluationResult<GfxObject> EvaluateDocument()
    {
        var props = new Dictionary<string, GfxValue>(_rootOrderedKeys.Count, StringComparer.Ordinal);
        foreach (var key in _rootOrderedKeys)
        {
            var value = ResolvePropertyPath(key);
            if (value is not null)
            {
                props[key] = UnwrapPropertyValue(value, _rootAstProperties[key].Span);
            }
        }

        var rootObject = CoerceObjectIfApplicable(new GfxObject(props, _rootOrderedKeys.AsReadOnly()));
        return new EvaluationResult<GfxObject>((GfxObject)rootObject, _diagnostics.AsReadOnly());
    }

    /// <summary>Evaluates a specific dot-separated property path (e.g., <c>"layers.layer1.size"</c> or <c>"output.dimensions"</c>) lazily.</summary>
    public EvaluationResult<GfxValue> EvaluatePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var span = _context.Document?.Span ?? new SourceSpan("<memory>", new SourceLocation(1, 1, 0), new SourceLocation(1, 1, 0));
        var value = ResolvePropertyPath(path, span);
        if (value is not null)
        {
            value = UnwrapPropertyValue(value, span);
        }

        return new EvaluationResult<GfxValue>(value, _diagnostics.AsReadOnly());
    }

    /// <summary>Evaluates an arbitrary AST expression node within the current context.</summary>
    public EvaluationResult<GfxValue> EvaluateExpression(GfxExpr expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        var value = EvaluateExprInternal(expression, currentPathPrefix: null, isMemberTarget: false);
        if (value is not null)
        {
            value = UnwrapPropertyValue(value, expression.Span);
        }

        return new EvaluationResult<GfxValue>(value, _diagnostics.AsReadOnly());
    }

    private GfxValue? ResolvePropertyPath(string path, SourceSpan? referenceSpan = null)
    {
        if (_resolvedPathCache.TryGetValue(path, out var cached))
        {
            return cached;
        }

        var segments = path.Split('.');
        if (segments.Length == 0)
        {
            return null;
        }

        if (!_rootAstProperties.TryGetValue(segments[0], out var currentPropNode))
        {
            return null;
        }

        if (segments.Length == 1)
        {
            return EvaluatePropertyNode(path, currentPropNode, parentPathPrefix: null);
        }

        // Traverse nested AST ObjectBlockExpr nodes lazily if possible so unrelated sibling properties aren't evaluated prematurely
        var currentPrefix = segments[0];
        for (var i = 1; i < segments.Length; i++)
        {
            var segment = segments[i];
            if (currentPropNode.Value is ObjectBlockExpr blockExpr)
            {
                var childProp = blockExpr.Properties.LastOrDefault(p => p.Name == segment);
                if (childProp is null)
                {
                    AddDiagnostic(
                        "EVAL003",
                        $"Property '{segment}' does not exist on '{currentPrefix}'.",
                        referenceSpan ?? blockExpr.Span);
                    return null;
                }

                var childPath = $"{currentPrefix}.{segment}";
                if (i == segments.Length - 1)
                {
                    return EvaluatePropertyNode(childPath, childProp, currentPrefix);
                }

                currentPropNode = childProp;
                currentPrefix = childPath;
            }
            else
            {
                // The intermediate node is a computed expression (e.g., an alias `alias = output`), so evaluate it and resolve remaining segments
                var intermediateValue = EvaluatePropertyNode(currentPrefix, currentPropNode, GetParentPrefix(currentPrefix));
                if (intermediateValue is null)
                {
                    return null;
                }

                for (var j = i; j < segments.Length; j++)
                {
                    intermediateValue = AccessMember(intermediateValue, segments[j], referenceSpan ?? currentPropNode.Span);
                    if (intermediateValue is null)
                    {
                        return null;
                    }
                }

                _resolvedPathCache[path] = intermediateValue;
                return intermediateValue;
            }
        }

        return null;
    }

    private GfxValue? EvaluatePropertyNode(string fullPath, PropertyAssignmentNode node, string? parentPathPrefix)
    {
        if (_resolvedPathCache.TryGetValue(fullPath, out var cached))
        {
            return cached;
        }

        if (!_activePaths.Add(fullPath))
        {
            var cycleStartIndex = _evaluationStack.IndexOf(fullPath);
            var cycleChain = cycleStartIndex >= 0
                ? string.Join(" -> ", _evaluationStack.Skip(cycleStartIndex).Append(fullPath))
                : $"{fullPath} -> {fullPath}";
            AddDiagnostic("EVAL001", $"Circular property reference detected: {cycleChain}.", node.Span);
            return null;
        }

        _evaluationStack.Add(fullPath);
        var previousRandom = _context.Random;
        var previousFrameRandom = _context.FrameRandom;
        if (_seedProvider is not null && parentPathPrefix == "layers")
        {
            var layerId = fullPath["layers.".Length..];
            _context.Random = _seedProvider.CreateLayerRandom(_context.Run.Number, $"{layerId}:props");
            _context.FrameRandom = _seedProvider.CreateRandom(_context.Run.Number, _context.Frame.Number, $"{layerId}:frame_props");
        }

        try
        {
            var rawValue = EvaluateExprInternal(node.Value, fullPath, isMemberTarget: false);
            if (rawValue is null)
            {
                return null;
            }

            var finalValue = UnwrapPropertyValue(rawValue, node.Span);
            // Only cache values that aren't backed by dynamic per-pass function references
            if (rawValue is not GfxFunctionRef)
            {
                _resolvedPathCache[fullPath] = finalValue;
            }

            return finalValue;
        }
        finally
        {
            _context.Random = previousRandom;
            _context.FrameRandom = previousFrameRandom;
            _evaluationStack.RemoveAt(_evaluationStack.Count - 1);
            _activePaths.Remove(fullPath);
        }
    }

    private GfxValue? EvaluateExprInternal(GfxExpr expr, string? currentPathPrefix, bool isMemberTarget)
    {
        switch (expr)
        {
            case NumberLiteralExpr num:
                return new GfxNumber(num.Value);

            case StringLiteralExpr str:
                return new GfxString(str.Value);

            case BooleanLiteralExpr boolean:
                return new GfxBoolean(boolean.Value);

            case InterpolatedStringExpr interpolated:
                return EvaluateInterpolatedString(interpolated, currentPathPrefix);

            case ArrayExpr array:
                return EvaluateArray(array, currentPathPrefix);

            case ObjectBlockExpr block:
                return EvaluateObjectBlock(block, currentPathPrefix);

            case IdentifierExpr identifier:
                return EvaluateIdentifier(identifier, isMemberTarget);

            case MemberAccessExpr memberAccess:
                return EvaluateMemberAccess(memberAccess, currentPathPrefix);

            case UnaryExpr unary:
                return EvaluateUnary(unary, currentPathPrefix);

            case BinaryExpr binary:
                return EvaluateBinary(binary, currentPathPrefix);

            case FunctionCallExpr call:
                return EvaluateFunctionCall(call, currentPathPrefix);

            default:
                AddDiagnostic("EVAL009", $"Unsupported expression type '{expr.GetType().Name}'.", expr.Span);
                return null;
        }
    }

    private GfxValue? EvaluateInterpolatedString(InterpolatedStringExpr interpolated, string? currentPathPrefix)
    {
        var sb = new StringBuilder();
        foreach (var part in interpolated.Parts)
        {
            switch (part)
            {
                case InterpolatedTextPart textPart:
                    sb.Append(textPart.Text);
                    break;

                case InterpolatedExpressionPart exprPart:
                {
                    var val = EvaluateExprInternal(exprPart.Expression, currentPathPrefix, isMemberTarget: false);
                    if (val is null)
                    {
                        return null;
                    }

                    val = UnwrapPropertyValue(val, exprPart.Span);
                    try
                    {
                        sb.Append(val.ToDisplayString(exprPart.Format));
                    }
                    catch (FormatException ex)
                    {
                        AddDiagnostic("EVAL004", $"Invalid interpolation format specifier '{exprPart.Format}': {ex.Message}", exprPart.Span);
                        return null;
                    }

                    break;
                }
            }
        }

        return new GfxString(sb.ToString());
    }

    private GfxValue? EvaluateArray(ArrayExpr array, string? currentPathPrefix)
    {
        var items = new List<GfxValue>(array.Elements.Count);
        for (var i = 0; i < array.Elements.Count; i++)
        {
            var item = EvaluateExprInternal(array.Elements[i], currentPathPrefix, isMemberTarget: false);
            if (item is null)
            {
                return null;
            }

            items.Add(UnwrapPropertyValue(item, array.Elements[i].Span));
        }

        return new GfxArray(items.AsReadOnly());
    }

    private GfxValue? EvaluateObjectBlock(ObjectBlockExpr block, string? currentPathPrefix)
    {
        var props = new Dictionary<string, GfxValue>(block.Properties.Count, StringComparer.Ordinal);
        var keys = new List<string>(block.Properties.Count);

        foreach (var prop in block.Properties)
        {
            var propPath = string.IsNullOrEmpty(currentPathPrefix) ? prop.Name : $"{currentPathPrefix}.{prop.Name}";
            var val = EvaluatePropertyNode(propPath, prop, currentPathPrefix);
            if (val is null)
            {
                return null;
            }

            if (!props.ContainsKey(prop.Name))
            {
                keys.Add(prop.Name);
            }

            props[prop.Name] = val;
        }

        return CoerceObjectIfApplicable(new GfxObject(props, keys.AsReadOnly()));
    }

    private GfxValue? EvaluateIdentifier(IdentifierExpr identifier, bool isMemberTarget)
    {
        var name = identifier.Name;

        // 1. Context variables (unless overridden by a user variable)
        if (_context.Variables.TryGetValue(name, out var variableValue))
        {
            return variableValue;
        }

        if (name == "run")
        {
            return _context.Run.ToGfxObject();
        }

        if (name == "frame")
        {
            return _context.Frame.ToGfxObject();
        }

        // 2. Built-in root constants (NAMED_COLORS, ALIGN, PI, TAU, E)
        if (BuiltInConstants.RootConstants.TryGetValue(name, out var constantValue))
        {
            return constantValue;
        }

        // 3. Top-level document properties
        if (_rootAstProperties.ContainsKey(name))
        {
            return ResolvePropertyPath(name, identifier.Span);
        }

        // 4. Shorthand `t` for frame.progress if not defined as a top-level property
        if (name == "t" && !isMemberTarget)
        {
            return new GfxNumber(_context.Frame.Progress);
        }

        // 5. Registered functions / JS function references
        if (_context.TryGetFunction(name, out var callable))
        {
            return new GfxFunctionRef(callable);
        }

        // 6. If used as the left-hand target of a member access (`UNKNOWN.FOO`), report an undefined identifier error
        if (isMemberTarget)
        {
            AddDiagnostic("EVAL002", $"Undefined object or namespace '{name}'.", identifier.Span);
            return null;
        }

        // 7. Bare unquoted identifier in value position (e.g., `type = fill`, `mix = multiply`) -> GfxSymbol
        return new GfxSymbol(name);
    }

    private GfxValue? EvaluateMemberAccess(MemberAccessExpr memberAccess, string? currentPathPrefix)
    {
        // Try to extract a static dot-path (e.g., `output.dimensions`) if rooted at a top-level document property
        if (TryExtractDotPath(memberAccess, out var dotPath))
        {
            var rootSegment = dotPath.Split('.')[0];
            if (_rootAstProperties.ContainsKey(rootSegment))
            {
                return ResolvePropertyPath(dotPath, memberAccess.Span);
            }
        }

        var target = EvaluateExprInternal(memberAccess.Target, currentPathPrefix, isMemberTarget: true);
        if (target is null)
        {
            return null;
        }

        target = UnwrapPropertyValue(target, memberAccess.Target.Span);
        return AccessMember(target, memberAccess.MemberName, memberAccess.Span);
    }

    private GfxValue? AccessMember(GfxValue target, string memberName, SourceSpan span)
    {
        switch (target)
        {
            case GfxObject obj:
                if (obj.Properties.TryGetValue(memberName, out var memberValue))
                {
                    return memberValue;
                }

                AddDiagnostic("EVAL003", $"Property '{memberName}' does not exist on object.", span);
                return null;

            case GfxDimensions dims:
                return memberName switch
                {
                    "width" => new GfxNumber(dims.Dimensions.Width),
                    "height" => new GfxNumber(dims.Dimensions.Height),
                    _ => ReportMissingMember("Dimensions", memberName, span)
                };

            case GfxColor color:
                return memberName switch
                {
                    "r" or "R" => new GfxNumber(color.Color.R),
                    "g" or "G" => new GfxNumber(color.Color.G),
                    "b" or "B" => new GfxNumber(color.Color.B),
                    "a" or "A" => new GfxNumber(color.Color.A),
                    _ => ReportMissingMember("Color", memberName, span)
                };

            default:
                AddDiagnostic("EVAL003", $"Cannot access member '{memberName}' on value of type '{target.GetType().Name}'.", span);
                return null;
        }
    }

    private GfxValue? ReportMissingMember(string typeName, string memberName, SourceSpan span)
    {
        AddDiagnostic("EVAL003", $"Property '{memberName}' does not exist on '{typeName}'.", span);
        return null;
    }

    private GfxValue? EvaluateUnary(UnaryExpr unary, string? currentPathPrefix)
    {
        var operand = EvaluateExprInternal(unary.Operand, currentPathPrefix, isMemberTarget: false);
        if (operand is null)
        {
            return null;
        }

        operand = UnwrapPropertyValue(operand, unary.Operand.Span);
        if (operand is not GfxNumber num)
        {
            AddDiagnostic("EVAL005", $"Unary operator '{unary.Operator}' requires a numeric operand, but got '{operand.GetType().Name}'.", unary.Span);
            return null;
        }

        return unary.Operator switch
        {
            UnaryOperator.Plus => num,
            UnaryOperator.Minus => new GfxNumber(-num.Value),
            _ => num
        };
    }

    private GfxValue? EvaluateBinary(BinaryExpr binary, string? currentPathPrefix)
    {
        var left = EvaluateExprInternal(binary.Left, currentPathPrefix, isMemberTarget: false);
        var right = EvaluateExprInternal(binary.Right, currentPathPrefix, isMemberTarget: false);
        if (left is null || right is null)
        {
            return null;
        }

        left = UnwrapPropertyValue(left, binary.Left.Span);
        right = UnwrapPropertyValue(right, binary.Right.Span);

        if (binary.Operator == BinaryOperator.Add && (left is GfxString || right is GfxString))
        {
            return new GfxString(left.ToDisplayString() + right.ToDisplayString());
        }

        if (left is not GfxNumber leftNum || right is not GfxNumber rightNum)
        {
            AddDiagnostic(
                "EVAL006",
                $"Binary operator '{binary.Operator}' requires numeric operands, but got '{left.GetType().Name}' and '{right.GetType().Name}'.",
                binary.Span);
            return null;
        }

        if (binary.Operator is BinaryOperator.Divide or BinaryOperator.Modulo && Math.Abs(rightNum.Value) < double.Epsilon)
        {
            AddDiagnostic("EVAL007", "Division or modulo by zero.", binary.Span);
            return null;
        }

        var result = binary.Operator switch
        {
            BinaryOperator.Add => leftNum.Value + rightNum.Value,
            BinaryOperator.Subtract => leftNum.Value - rightNum.Value,
            BinaryOperator.Multiply => leftNum.Value * rightNum.Value,
            BinaryOperator.Divide => leftNum.Value / rightNum.Value,
            BinaryOperator.Modulo => leftNum.Value % rightNum.Value,
            _ => 0.0
        };

        return new GfxNumber(result);
    }

    private GfxValue? EvaluateFunctionCall(FunctionCallExpr call, string? currentPathPrefix)
    {
        IGfxCallable? callable = null;

        if (call.Callee is IdentifierExpr idExpr)
        {
            if (!_context.TryGetFunction(idExpr.Name, out callable))
            {
                // Also check if a top-level variable or property holds a GfxFunctionRef
                var resolvedCallee = EvaluateIdentifier(idExpr, isMemberTarget: false);
                if (resolvedCallee is GfxFunctionRef fnRef)
                {
                    callable = fnRef.Callable;
                }
            }
        }
        else
        {
            var targetVal = EvaluateExprInternal(call.Callee, currentPathPrefix, isMemberTarget: true);
            if (targetVal is GfxFunctionRef fnRef)
            {
                callable = fnRef.Callable;
            }
        }

        if (callable is null)
        {
            AddDiagnostic("EVAL008", $"Unknown function '{call.FunctionName}'.", call.Span);
            return null;
        }

        var evaluatedArgs = new List<GfxValue>(call.Arguments.Count);
        for (var argumentIndex = 0; argumentIndex < call.Arguments.Count; argumentIndex++)
        {
            var argExpr = call.Arguments[argumentIndex];
            // Inline object/array arguments need their own memoization namespace. Reusing the
            // caller's property prefix makes distinct objects such as color-ramp stops share
            // child cache paths (for example, both can otherwise resolve as "ramp.pos").
            var argumentPathPrefix = $"{currentPathPrefix ?? "$root"}.$call@{call.Span.Start.Offset}.arg{argumentIndex}";
            var argVal = EvaluateExprInternal(argExpr, argumentPathPrefix, isMemberTarget: false);
            if (argVal is null)
            {
                return null;
            }

            evaluatedArgs.Add(UnwrapPropertyValue(argVal, argExpr.Span));
        }

        try
        {
            return callable.Invoke(evaluatedArgs.AsReadOnly(), _context, call.Span);
        }
        catch (GfxException ex)
        {
            _diagnostics.AddRange(ex.Diagnostics);
            return null;
        }
    }

    private GfxValue UnwrapPropertyValue(GfxValue value, SourceSpan span)
    {
        if (value is GfxFunctionRef fnRef)
        {
            try
            {
                return fnRef.Callable.Invoke([], _context, span);
            }
            catch (GfxException ex)
            {
                _diagnostics.AddRange(ex.Diagnostics);
            }
        }

        return value;
    }

    private static GfxValue CoerceObjectIfApplicable(GfxObject obj)
    {
        // Automatically coerce `{ width = 64, height = 64 }` into GfxDimensions when it contains only positive numeric width and height
        if (obj.Properties.Count == 2
            && obj.Properties.ContainsKey("width")
            && obj.Properties.ContainsKey("height")
            && obj.TryCoerceToDimensions(out var dims))
        {
            return new GfxDimensions(dims);
        }

        return obj;
    }

    private static bool TryExtractDotPath(GfxExpr expr, out string path)
    {
        var parts = new Stack<string>();
        var current = expr;
        while (current is MemberAccessExpr member)
        {
            parts.Push(member.MemberName);
            current = member.Target;
        }

        if (current is IdentifierExpr id)
        {
            parts.Push(id.Name);
            path = string.Join('.', parts);
            return true;
        }

        path = string.Empty;
        return false;
    }

    private static string? GetParentPrefix(string path)
    {
        var lastDot = path.LastIndexOf('.');
        return lastDot > 0 ? path[..lastDot] : null;
    }

    private void AddDiagnostic(string code, string message, SourceSpan span)
    {
        _diagnostics.Add(new GfxDiagnostic(code, message, span, DiagnosticSeverity.Error));
    }
}
