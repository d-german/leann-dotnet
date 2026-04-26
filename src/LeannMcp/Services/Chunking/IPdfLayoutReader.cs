using CSharpFunctionalExtensions;
using LeannMcp.Models;

namespace LeannMcp.Services.Chunking;

/// <summary>
/// Optional capability — implemented by readers that can expose per-line
/// font-size metadata so <see cref="HeadingDetector"/> can promote section
/// titles into first-class chunk metadata.
/// <para/>
/// Kept narrow per ISP: the generic <see cref="IDocumentReader"/> /
/// <see cref="IStructuredDocumentReader"/> contracts are unchanged for
/// readers (Markdown, code) where font-size has no meaning.
/// </summary>
public interface IPdfLayoutReader
{
    Result<IReadOnlyList<(int PageNumber, IReadOnlyList<PageLine> Lines)>> ReadLayout(string filePath);
}
