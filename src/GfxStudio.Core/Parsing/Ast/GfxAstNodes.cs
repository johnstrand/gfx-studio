using GfxStudio.Core.Diagnostics;

namespace GfxStudio.Core.Parsing.Ast;

/// <summary>Base class for all nodes in the .gfx abstract syntax tree.</summary>
public abstract record GfxAstNode(SourceSpan Span);

/// <summary>Root AST node representing a parsed .gfx document.</summary>
public sealed record GfxDocument(
    IReadOnlyList<PropertyAssignmentNode> Properties,
    SourceSpan Span) : GfxAstNode(Span);

/// <summary>Represents a key-value property assignment (<c>name = expr</c> or <c>name: expr</c>).</summary>
public sealed record PropertyAssignmentNode(
    string Name,
    GfxExpr Value,
    SourceSpan Span) : GfxAstNode(Span);

/// <summary>Base class for all .gfx expression AST nodes.</summary>
public abstract record GfxExpr(SourceSpan Span) : GfxAstNode(Span);

/// <summary>An object block expression (<c>{ key = value, ... }</c>).</summary>
public sealed record ObjectBlockExpr(
    IReadOnlyList<PropertyAssignmentNode> Properties,
    SourceSpan Span) : GfxExpr(Span);

/// <summary>A list/array literal expression (<c>[ elem1, elem2, ... ]</c>).</summary>
public sealed record ArrayExpr(
    IReadOnlyList<GfxExpr> Elements,
    SourceSpan Span) : GfxExpr(Span);

/// <summary>A numeric literal expression (integer or floating-point).</summary>
public sealed record NumberLiteralExpr(
    double Value,
    bool IsInteger,
    SourceSpan Span) : GfxExpr(Span);

/// <summary>A plain string literal expression.</summary>
public sealed record StringLiteralExpr(
    string Value,
    SourceSpan Span) : GfxExpr(Span);

/// <summary>A boolean literal expression (<c>true</c>/<c>false</c> or <c>True</c>/<c>False</c>).</summary>
public sealed record BooleanLiteralExpr(
    bool Value,
    SourceSpan Span) : GfxExpr(Span);

/// <summary>Base class for segments within an <see cref="InterpolatedStringExpr"/>.</summary>
public abstract record InterpolatedPart(SourceSpan Span) : GfxAstNode(Span);

/// <summary>A literal text segment inside an interpolated string.</summary>
public sealed record InterpolatedTextPart(
    string Text,
    SourceSpan Span) : InterpolatedPart(Span);

/// <summary>An interpolated expression segment (<c>${expr}</c> or <c>${expr:format}</c>).</summary>
public sealed record InterpolatedExpressionPart(
    GfxExpr Expression,
    string? Format,
    SourceSpan Span) : InterpolatedPart(Span);

/// <summary>An interpolated string literal containing text and embedded expressions.</summary>
public sealed record InterpolatedStringExpr(
    IReadOnlyList<InterpolatedPart> Parts,
    SourceSpan Span) : GfxExpr(Span);

/// <summary>An identifier reference or bare symbol expression.</summary>
public sealed record IdentifierExpr(
    string Name,
    SourceSpan Span) : GfxExpr(Span);

/// <summary>A dot-member access expression (<c>target.member</c>).</summary>
public sealed record MemberAccessExpr(
    GfxExpr Target,
    string MemberName,
    SourceSpan Span) : GfxExpr(Span);

/// <summary>A function invocation expression (<c>callee(arg1, arg2, ...)</c>).</summary>
public sealed record FunctionCallExpr(
    GfxExpr Callee,
    IReadOnlyList<GfxExpr> Arguments,
    SourceSpan Span) : GfxExpr(Span)
{
    public FunctionCallExpr(string functionName, IReadOnlyList<GfxExpr> arguments, SourceSpan span)
        : this(new IdentifierExpr(functionName, span), arguments, span)
    {
    }

    /// <summary>Gets the function name for simple or member-qualified calls.</summary>
    public string FunctionName => Callee switch
    {
        IdentifierExpr identifier => identifier.Name,
        MemberAccessExpr memberAccess => memberAccess.MemberName,
        _ => string.Empty
    };
}

/// <summary>Unary operators supported in .gfx expressions.</summary>
public enum UnaryOperator
{
    Plus,
    Minus
}

/// <summary>A prefix unary operator expression (<c>+operand</c> or <c>-operand</c>).</summary>
public sealed record UnaryExpr(
    UnaryOperator Operator,
    GfxExpr Operand,
    SourceSpan Span) : GfxExpr(Span);

/// <summary>Binary arithmetic operators supported in .gfx expressions.</summary>
public enum BinaryOperator
{
    Add,
    Subtract,
    Multiply,
    Divide,
    Modulo
}

/// <summary>An infix binary operator expression (<c>left op right</c>).</summary>
public sealed record BinaryExpr(
    GfxExpr Left,
    BinaryOperator Operator,
    GfxExpr Right,
    SourceSpan Span) : GfxExpr(Span);
