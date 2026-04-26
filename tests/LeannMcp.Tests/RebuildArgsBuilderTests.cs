using LeannMcp;
using Xunit;

namespace LeannMcp.Tests;

public class RebuildArgsBuilderTests
{
    [Fact]
    public void WithForce_AppendsForce_WhenMissing()
    {
        var input = new[] { "--rebuild", "--docs", "C:\\foo", "--chunk-size", "1024" };

        var result = RebuildArgsBuilder.WithForce(input);

        Assert.Contains("--force", result);
        // Original args preserved
        Assert.Contains("--rebuild", result);
        Assert.Contains("--docs", result);
        Assert.Contains("C:\\foo", result);
        Assert.Contains("--chunk-size", result);
        Assert.Contains("1024", result);
        Assert.Equal(input.Length + 1, result.Length);
    }

    [Fact]
    public void WithForce_DoesNotDuplicate_WhenForceAlreadyPresent()
    {
        var input = new[] { "--rebuild", "--force", "--docs", "C:\\foo" };

        var result = RebuildArgsBuilder.WithForce(input);

        Assert.Single(result, a => a == "--force");
        Assert.Equal(input.Length, result.Length);
    }

    [Fact]
    public void WithForce_DoesNotMutate_OriginalArray()
    {
        var input = new[] { "--rebuild", "--docs", "C:\\foo" };
        var originalLength = input.Length;

        _ = RebuildArgsBuilder.WithForce(input);

        Assert.Equal(originalLength, input.Length);
        Assert.DoesNotContain("--force", input);
    }
}
