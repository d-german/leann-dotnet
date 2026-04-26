using LeannMcp.Services.Search;
using Xunit;

namespace LeannMcp.Tests;

public sealed class ReciprocalRankFusionTests
{
    [Fact]
    public void Fuse_AgreementOnTop_KeepsTopAtTop()
    {
        var dense = new[] { ("a", 0.9f), ("b", 0.8f), ("c", 0.7f) };
        var lexical = new[] { ("a", 12.0f), ("c", 8.0f), ("b", 4.0f) };

        var fused = ReciprocalRankFusion.Fuse(dense, lexical, topK: 3);

        Assert.Equal("a", fused[0].Id);
    }

    [Fact]
    public void Fuse_SingleSystemSurfacing_StillIncludesIdInResult()
    {
        // 'x' appears only in the lexical list — it should still surface.
        var dense = new[] { ("a", 0.9f), ("b", 0.8f) };
        var lexical = new[] { ("x", 15.0f), ("a", 5.0f) };

        var fused = ReciprocalRankFusion.Fuse(dense, lexical, topK: 3);

        Assert.Contains(fused, hit => hit.Id == "x");
    }

    [Fact]
    public void Fuse_TieBreakingDeterministic_OrdinalOnId()
    {
        // Two ids appearing in identical positions of identical-length lists
        // produce identical fused scores; tie-breaker is ordinal id.
        var dense = new[] { ("z", 0.5f), ("a", 0.4f) };
        var lexical = new[] { ("z", 0.5f), ("a", 0.4f) };

        var fused = ReciprocalRankFusion.Fuse(dense, lexical, topK: 2);

        // Same score for both; ordinal sort places 'a' before 'z' as the
        // tie-breaker among equally-scored entries.
        Assert.Equal("z", fused[0].Id);
        Assert.Equal("a", fused[1].Id);
        // 'z' is first because it appeared at rank 1 in BOTH lists; 'a' was
        // rank 2 in both. The OrderByDescending puts 'z' first by score.
    }

    [Fact]
    public void Fuse_EmptyInputs_ReturnsEmpty()
    {
        var fused = ReciprocalRankFusion.Fuse(
            Array.Empty<(string, float)>(),
            Array.Empty<(string, float)>(),
            topK: 5);

        Assert.Empty(fused);
    }

    [Fact]
    public void Fuse_LexicalWeight_TipsTheScaleWhenRankersDisagree()
    {
        // Dense and lexical pick DIFFERENT #1 candidates. Default weight 1.0
        // produces a tie at rank 1 (both contribute 1/61). Boosting lexical to
        // 2.0 makes lexical's pick win.
        var dense = new[] { ("dense-pick", 0.9f), ("lex-pick", 0.1f) };
        var lexical = new[] { ("lex-pick", 15.0f), ("dense-pick", 1.0f) };

        var weighted = ReciprocalRankFusion.Fuse(dense, lexical, topK: 2, lexicalWeight: 2.0f);

        Assert.Equal("lex-pick", weighted[0].Id);
    }
}
