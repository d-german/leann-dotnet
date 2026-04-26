using System.Text;
using LeannMcp.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LeannMcp.Services.Chunking;

/// <summary>
/// AST-aware chunker for C# source files using Roslyn (Microsoft.CodeAnalysis.CSharp).
/// Emits one chunk per top-level member (method, constructor, property, indexer, operator,
/// event, finalizer, conversion) with a context-comment prefix so the embedder sees
/// structural information. For type declarations with no member children (data classes,
/// marker interfaces), emits the whole type as a single chunk.
/// Falls back to emitting whole file as a single chunk if Roslyn cannot parse anything.
/// </summary>
public sealed class RoslynChunker : ICodeChunkStrategy
{
    public bool CanHandle(string? language) =>
        string.Equals(language, "csharp", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<string> Chunk(string content, ChunkingOptions options)
    {
        if (string.IsNullOrWhiteSpace(content))
            return [];

        var tree = CSharpSyntaxTree.ParseText(content);
        var root = tree.GetRoot();

        var chunks = new List<string>();
        CollectMemberChunks(root, currentNamespace: null, currentType: null, content, chunks);

        if (chunks.Count == 0)
        {
            // No members found (e.g., file with only top-level statements or directives).
            // Return the whole content as one chunk so it isn't lost.
            var trimmed = content.Trim();
            if (trimmed.Length > 0) chunks.Add(trimmed);
        }

        return chunks;
    }

    private static void CollectMemberChunks(
        SyntaxNode node, string? currentNamespace, string? currentType,
        string sourceText, List<string> chunks)
    {
        foreach (var child in node.ChildNodes())
        {
            switch (child)
            {
                case BaseNamespaceDeclarationSyntax ns:
                    CollectMemberChunks(ns, ns.Name.ToString(), currentType, sourceText, chunks);
                    break;

                case TypeDeclarationSyntax type:
                    HandleTypeDeclaration(type, currentNamespace, currentType, sourceText, chunks);
                    break;

                case EnumDeclarationSyntax enumDecl:
                    chunks.Add(BuildChunk(currentNamespace, currentType, enumDecl.Identifier.Text, enumDecl));
                    break;

                case DelegateDeclarationSyntax del:
                    chunks.Add(BuildChunk(currentNamespace, currentType, del.Identifier.Text, del));
                    break;

                default:
                    // Recurse so we still find types nested under file-scoped constructs.
                    CollectMemberChunks(child, currentNamespace, currentType, sourceText, chunks);
                    break;
            }
        }
    }

    private static void HandleTypeDeclaration(
        TypeDeclarationSyntax type, string? currentNamespace, string? parentType,
        string sourceText, List<string> chunks)
    {
        var typeName = parentType is null ? type.Identifier.Text : $"{parentType}.{type.Identifier.Text}";
        var memberChunks = new List<string>();

        foreach (var member in type.Members)
        {
            var chunk = TryBuildMemberChunk(member, currentNamespace, typeName);
            if (chunk is not null)
                memberChunks.Add(chunk);
            else if (member is TypeDeclarationSyntax nestedType)
                HandleTypeDeclaration(nestedType, currentNamespace, typeName, sourceText, chunks);
        }

        if (memberChunks.Count == 0)
        {
            // Empty type or only nested types — emit the full declaration so it isn't lost.
            chunks.Add(BuildChunk(currentNamespace, parentType, type.Identifier.Text, type));
        }
        else
        {
            chunks.AddRange(memberChunks);
        }
    }

    private static string? TryBuildMemberChunk(MemberDeclarationSyntax member, string? ns, string typeName)
    {
        return member switch
        {
            MethodDeclarationSyntax m => BuildChunk(ns, typeName, m.Identifier.Text, m),
            ConstructorDeclarationSyntax c => BuildChunk(ns, typeName, $".ctor/{c.Identifier.Text}", c),
            DestructorDeclarationSyntax d => BuildChunk(ns, typeName, $"~{d.Identifier.Text}", d),
            PropertyDeclarationSyntax p => BuildChunk(ns, typeName, p.Identifier.Text, p),
            IndexerDeclarationSyntax i => BuildChunk(ns, typeName, "this[]", i),
            OperatorDeclarationSyntax o => BuildChunk(ns, typeName, $"operator {o.OperatorToken.Text}", o),
            ConversionOperatorDeclarationSyntax co => BuildChunk(ns, typeName, $"op_{co.Type}", co),
            EventDeclarationSyntax ev => BuildChunk(ns, typeName, ev.Identifier.Text, ev),
            EventFieldDeclarationSyntax evf => BuildChunk(ns, typeName, evf.Declaration.Variables.FirstOrDefault()?.Identifier.Text ?? "event", evf),
            FieldDeclarationSyntax f when f.Declaration.Variables.Count > 0 =>
                BuildChunk(ns, typeName, f.Declaration.Variables[0].Identifier.Text, f),
            _ => null,
        };
    }

    private static string BuildChunk(string? ns, string? typeName, string memberName, SyntaxNode node)
    {
        var qualifier = (ns, typeName) switch
        {
            (not null, not null) => $"{ns}.{typeName}.{memberName}",
            (not null, null)     => $"{ns}.{memberName}",
            (null, not null)     => $"{typeName}.{memberName}",
            _                    => memberName,
        };

        var sb = new StringBuilder();
        sb.Append("// ").AppendLine(qualifier);
        sb.Append(node.ToFullString().Trim());
        return sb.ToString();
    }
}
