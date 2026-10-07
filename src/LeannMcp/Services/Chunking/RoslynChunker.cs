using System.Text;
using System.Text.RegularExpressions;
using LeannMcp.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LeannMcp.Services.Chunking;

/// <summary>
/// AST-aware chunker for C# source files using Roslyn (Microsoft.CodeAnalysis.CSharp).
/// </summary>
/// <remarks>
/// Passages are sized to what the embedder can read, not to syntax. One passage per member
/// made half of all passages a single field or auto-property, each costing a full embedding
/// while saying almost nothing, and left long methods as one passage whose text past the
/// model's token window was never embedded. Instead, consecutive members of one type are
/// packed into a passage up to <see cref="ChunkingOptions.CodeChunkSize"/> characters, and a
/// member longer than that is split at line boundaries into overlapping parts, each labelled
/// with the member it came from. Every passage starts with a comment naming its type, and
/// members are never mixed across types.
///
/// Files are parsed as modern .NET and, when their conditionals test target frameworks, also
/// as .NET Framework 4.8; members found by either parse are kept, so code that only one
/// build compiles is not hidden as inactive text.
/// </remarks>
public sealed class RoslynChunker : ICodeChunkStrategy
{
    private static readonly Regex FrameworkConditional = new(
        @"^[ \t]*#[ \t]*(?:if|elif)\b[^\r\n]*\bNET(?:FRAMEWORK|COREAPP|STANDARD|\d|\b)",
        RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly CSharpParseOptions ModernDotNet = CSharpParseOptions.Default
        .WithPreprocessorSymbols(new[] { "DEBUG", "TRACE", "NET", "NETCOREAPP" }
            .Concat(Enumerable.Range(5, 6).Select(v => $"NET{v}_0_OR_GREATER"))
            .ToArray());

    private static readonly CSharpParseOptions DotNetFramework = CSharpParseOptions.Default
        .WithPreprocessorSymbols(new[] { "DEBUG", "TRACE", "NETFRAMEWORK", "NET48" }
            .Concat(new[] { "20", "30", "35", "40", "45", "451", "452", "46", "461", "462", "47", "471", "472", "48" }
                .Select(v => $"NET{v}_OR_GREATER"))
            .ToArray());

    public bool CanHandle(string? language) =>
        string.Equals(language, "csharp", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<string> Chunk(string content, ChunkingOptions options)
    {
        if (string.IsNullOrWhiteSpace(content))
            return [];

        int maxChars = Math.Max(256, options.CodeChunkSize);
        int overlapChars = Math.Clamp(options.CodeChunkOverlap, 0, maxChars / 2);

        var units = CollectUnits(content, ModernDotNet);
        if (FrameworkConditional.IsMatch(content))
        {
            var seen = units.Select(u => u.SpanStart).ToHashSet();
            units.AddRange(CollectUnits(content, DotNetFramework).Where(u => seen.Add(u.SpanStart)));
            units.Sort((a, b) => a.SpanStart.CompareTo(b.SpanStart));
        }

        if (units.Count == 0)
        {
            // Nothing declarative (top-level statements, directives only): keep the text.
            var trimmed = content.Trim();
            return trimmed.Length == 0 ? [] : SplitLines(trimmed, "", maxChars, overlapChars);
        }

        return Pack(units, maxChars, overlapChars);
    }

    /// <summary>One declaration to place: the type it belongs to, its name, and its text.</summary>
    private sealed record Unit(string Container, string Name, string Text, int SpanStart);

    private static List<Unit> CollectUnits(string content, CSharpParseOptions options)
    {
        var root = CSharpSyntaxTree.ParseText(content, options).GetRoot();
        var units = new List<Unit>();
        Collect(root, ns: null, type: null, units);
        return units;
    }

    private static void Collect(SyntaxNode node, string? ns, string? type, List<Unit> units)
    {
        foreach (var child in node.ChildNodes())
        {
            switch (child)
            {
                case BaseNamespaceDeclarationSyntax nsDecl:
                    Collect(nsDecl, Qualify(ns, nsDecl.Name.ToString()), type, units);
                    break;

                case TypeDeclarationSyntax typeDecl:
                    CollectType(typeDecl, ns, type, units);
                    break;

                case EnumDeclarationSyntax or DelegateDeclarationSyntax:
                    var member = (MemberDeclarationSyntax)child;
                    units.Add(new Unit(Qualify(ns, type), NameOf(member), TextOf(member), member.SpanStart));
                    break;

                default:
                    Collect(child, ns, type, units);
                    break;
            }
        }
    }

    private static void CollectType(TypeDeclarationSyntax typeDecl, string? ns, string? parentType, List<Unit> units)
    {
        string typeName = parentType is null ? typeDecl.Identifier.Text : $"{parentType}.{typeDecl.Identifier.Text}";
        string container = Qualify(ns, typeName);
        int before = units.Count;

        foreach (var member in typeDecl.Members)
        {
            if (member is TypeDeclarationSyntax nested)
                CollectType(nested, ns, typeName, units);
            else
                units.Add(new Unit(container, NameOf(member), TextOf(member), member.SpanStart));
        }

        if (units.Count == before)
        {
            // A type with no members (marker interface, empty class): the declaration itself.
            units.Add(new Unit(Qualify(ns, parentType), typeDecl.Identifier.Text, TextOf(typeDecl), typeDecl.SpanStart));
        }
    }

    /// <summary>
    /// Packs consecutive units of one container into passages of at most
    /// <paramref name="maxChars"/>; a unit too long for one passage is split on its own.
    /// </summary>
    private static List<string> Pack(List<Unit> units, int maxChars, int overlapChars)
    {
        var passages = new List<string>();
        var current = new StringBuilder();
        string? currentContainer = null;

        void Flush()
        {
            if (current.Length > 0)
            {
                passages.Add(current.ToString().TrimEnd());
                current.Clear();
            }
        }

        foreach (var unit in units)
        {
            string header = $"// {unit.Container}";
            if (header.Length + 1 + unit.Text.Length > maxChars)
            {
                Flush();
                currentContainer = null;
                passages.AddRange(SplitLines(unit.Text, $"// {Qualify(unit.Container, unit.Name)}", maxChars, overlapChars));
                continue;
            }

            if (unit.Container != currentContainer || current.Length + unit.Text.Length + 2 > maxChars)
            {
                Flush();
                current.AppendLine(header);
                currentContainer = unit.Container;
            }
            else
            {
                current.AppendLine();
            }
            current.AppendLine(unit.Text);
        }

        Flush();
        return passages;
    }

    /// <summary>
    /// Splits <paramref name="text"/> at line boundaries into parts that fit
    /// <paramref name="maxChars"/> with their header, repeating about
    /// <paramref name="overlapChars"/> of trailing lines at the start of the next part.
    /// </summary>
    private static List<string> SplitLines(string text, string header, int maxChars, int overlapChars)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        int budget = Math.Max(64, maxChars - header.Length - 24);
        var parts = new List<List<string>>();
        var part = new List<string>();
        int size = 0;

        foreach (string raw in lines)
        {
            // A single line longer than the budget (generated code, long literals) is cut.
            string line = raw.Length > budget ? raw[..budget] : raw;
            if (size + line.Length + 1 > budget && part.Count > 0)
            {
                parts.Add(part);
                var carry = new List<string>();
                int carried = 0;
                for (int i = part.Count - 1; i >= 0 && carried + part[i].Length + 1 <= overlapChars; i--)
                {
                    carry.Insert(0, part[i]);
                    carried += part[i].Length + 1;
                }
                part = carry;
                size = carried;
            }
            part.Add(line);
            size += line.Length + 1;
        }
        if (part.Count > 0)
            parts.Add(part);

        var result = new List<string>(parts.Count);
        for (int i = 0; i < parts.Count; i++)
        {
            string label = header.Length == 0
                ? ""
                : parts.Count == 1 ? header + "\n" : $"{header} (part {i + 1} of {parts.Count})\n";
            result.Add(label + string.Join("\n", parts[i]).Trim());
        }
        return result;
    }

    /// <summary>
    /// The member's code with its leading comments and doc comments, but without inactive
    /// <c>#if</c> text or directives, which belong to whichever parse made them active.
    /// </summary>
    private static string TextOf(SyntaxNode node)
    {
        var sb = new StringBuilder();
        foreach (var trivia in node.GetLeadingTrivia())
        {
            if (trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
                || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia)
                || trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
                || trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia))
            {
                sb.Append(trivia.ToFullString().TrimEnd()).Append('\n');
            }
        }
        sb.Append(node.ToString());
        return sb.ToString().Trim();
    }

    private static string NameOf(MemberDeclarationSyntax member) => member switch
    {
        MethodDeclarationSyntax m => m.Identifier.Text,
        ConstructorDeclarationSyntax c => $".ctor/{c.Identifier.Text}",
        DestructorDeclarationSyntax d => $"~{d.Identifier.Text}",
        PropertyDeclarationSyntax p => p.Identifier.Text,
        IndexerDeclarationSyntax => "this[]",
        OperatorDeclarationSyntax o => $"operator {o.OperatorToken.Text}",
        ConversionOperatorDeclarationSyntax co => $"op_{co.Type}",
        EventDeclarationSyntax ev => ev.Identifier.Text,
        BaseFieldDeclarationSyntax f => f.Declaration.Variables.FirstOrDefault()?.Identifier.Text ?? "field",
        EnumDeclarationSyntax e => e.Identifier.Text,
        DelegateDeclarationSyntax del => del.Identifier.Text,
        BaseTypeDeclarationSyntax t => t.Identifier.Text,
        _ => member.Kind().ToString(),
    };

    private static string Qualify(string? left, string? right) => (left, right) switch
    {
        (not null and not "", not null and not "") => $"{left}.{right}",
        (not null and not "", _) => left,
        (_, not null) => right,
        _ => "",
    };
}
