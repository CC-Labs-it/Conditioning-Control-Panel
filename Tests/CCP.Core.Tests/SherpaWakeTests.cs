using System;
using System.IO;
using ConditioningControlPanel.Services.Speech;
using Xunit;

namespace CCP.Core.Tests;

public sealed class SherpaWakeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FindsCompleteFlatAndNestedModelsAndPrefersInt8(bool nested)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var folder = nested ? Path.Combine(root, "model") : root;
        Directory.CreateDirectory(folder);
        try
        {
            foreach (var file in new[] { "encoder.onnx", "encoder.int8.onnx", "decoder.onnx", "decoder.int8.onnx", "joiner.onnx", "joiner.int8.onnx", "tokens.txt", "keywords.txt" })
                File.WriteAllText(Path.Combine(folder, file), "");
            var model = SherpaWakeService.FindModel(root);
            Assert.NotNull(model);
            Assert.EndsWith("encoder.int8.onnx", model.Value.Encoder);
            Assert.EndsWith("decoder.int8.onnx", model.Value.Decoder);
            Assert.EndsWith("joiner.int8.onnx", model.Value.Joiner);
            Assert.Equal(Path.Combine(folder, "tokens.txt"), model.Value.Tokens);
            Assert.Equal(Path.Combine(folder, "keywords.txt"), model.Value.Keywords);
            File.Delete(model.Value.Keywords);
            Assert.Null(SherpaWakeService.FindModel(root));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void MissingDirectoryHasNoModel() => Assert.Null(SherpaWakeService.FindModel(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())));
}
