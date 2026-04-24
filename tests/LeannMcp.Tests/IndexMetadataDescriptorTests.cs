using System.IO;
using System.Text.Json;
using LeannMcp.Models;
using LeannMcp.Services.Chunking;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LeannMcp.Tests;

public class IndexMetadataDescriptorTests
{
    [Theory]
    [InlineData(ModelRegistry.JinaCodeId)]
    [InlineData(ModelRegistry.ContrieverId)]
    public void PassageWriter_WritesActiveDescriptorIdAndDimensions(string modelId)
    {
        var descriptor = ModelRegistry.GetById(modelId).Value;
        var dir = Path.Combine(Path.GetTempPath(), "leann-meta-test-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            var writer = new PassageWriter(NullLogger<PassageWriter>.Instance, descriptor);
            var passages = new List<PassageData>
            {
                new("id1", "text", null)
            };

            var result = writer.WritePassages(dir, "test", passages, new[] { dir });
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error : "");

            var metaPath = Path.Combine(dir, "documents.leann.meta.json");
            var json = File.ReadAllText(metaPath);
            var meta = JsonSerializer.Deserialize<IndexMetadata>(json);

            Assert.NotNull(meta);
            Assert.Equal(descriptor.Id, meta!.EmbeddingModel);
            Assert.Equal(descriptor.Dimensions, meta.Dimensions);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
