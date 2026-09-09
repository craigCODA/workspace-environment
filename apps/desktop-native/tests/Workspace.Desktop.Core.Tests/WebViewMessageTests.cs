using System.Text;
using Workspace.Desktop.Core.Bridge;

namespace Workspace.Desktop.Core.Tests;

public sealed class WebViewMessageTests
{
    [Fact]
    public void Known_version_one_message_is_accepted()
    {
        var validator = new RendererMessageValidator();

        var result = validator.TryParse(
            """{"version":1,"type":"renderer.ready","payload":{"surface":"spatial"}}""",
            out var message,
            out var error);

        Assert.True(result, error);
        Assert.Equal("renderer.ready", message!.Type);
    }

    [Fact]
    public void Unknown_message_types_and_unexpected_top_level_properties_are_rejected()
    {
        var validator = new RendererMessageValidator();

        Assert.False(validator.TryParse(
            """{"version":1,"type":"native.runAnything","payload":{}}""",
            out _,
            out _));
        Assert.False(validator.TryParse(
            """{"version":1,"type":"renderer.ready","payload":{},"command":"format C:"}""",
            out _,
            out _));
    }

    [Fact]
    public void Messages_over_256_kib_are_rejected_before_execution()
    {
        var validator = new RendererMessageValidator();
        var oversized = "{\"version\":1,\"type\":\"agent.instruction\",\"payload\":{\"text\":\""
            + new string('x', RendererMessageValidator.MaximumMessageBytes)
            + "\"}}";

        Assert.True(Encoding.UTF8.GetByteCount(oversized) > RendererMessageValidator.MaximumMessageBytes);
        Assert.False(validator.TryParse(oversized, out _, out var error));
        Assert.Contains("256 KiB", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Scene_results_must_match_a_pending_native_request()
    {
        var validator = new RendererMessageValidator();
        validator.ExpectSceneResult("scene-7");

        Assert.False(validator.TryParse(
            """{"version":1,"type":"scene.command.result","payload":{"id":"scene-8","ok":true}}""",
            out _,
            out _));
        Assert.True(validator.TryParse(
            """{"version":1,"type":"scene.command.result","payload":{"id":"scene-7","ok":true}}""",
            out _,
            out var error), error);
    }
}
