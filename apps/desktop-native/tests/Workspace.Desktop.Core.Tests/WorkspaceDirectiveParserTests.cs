using System.Text.Json;
using Workspace.Desktop.Core.Runtime;

namespace Workspace.Desktop.Core.Tests;

public sealed class WorkspaceDirectiveParserTests
{
    [Fact]
    public void Parses_action_without_speaking_private_markup()
    {
        var result = WorkspaceDirectiveParser.Parse(
            "Opening it. [[workspace:{\"command\":\"application.open\",\"args\":{\"query\":\"Notepad\"}}]]");

        Assert.Equal("Opening it.", result.SpokenText);
        Assert.Equal("application.open", Assert.Single(result.Directives).Command);
    }

    [Fact]
    public void Accepts_host_application_identity_and_rejects_unrelated_entity_kinds()
    {
        var accepted = WorkspaceDirectiveParser.Parse(
            "Opening it. [[workspace:{\"command\":\"application.open\",\"args\":{\"applicationId\":\"pc.application:notepad\"}}]]");
        var rejected = WorkspaceDirectiveParser.Parse(
            "No. [[workspace:{\"command\":\"application.open\",\"args\":{\"applicationId\":\"app:notepad\"}}]]");

        Assert.Equal("pc.application:notepad", Assert.Single(accepted.Directives).Arguments.GetProperty("applicationId").GetString());
        Assert.Empty(rejected.Directives);
    }

    [Fact]
    public void Removes_unterminated_private_markup_and_handles_marker_text_inside_json_strings()
    {
        var embeddedMarker = WorkspaceDirectiveParser.Parse(
            "Before [[workspace:{\"command\":\"application.search\",\"args\":{\"query\":\"Note ]] pad\"}}]] after");
        var unterminated = WorkspaceDirectiveParser.Parse(
            "Before [[workspace:{\"command\":\"application.search\",\"args\":{\"query\":\"secret\"}}");

        Assert.Equal("Before after", embeddedMarker.SpokenText);
        Assert.Equal("Before", unterminated.SpokenText);
        Assert.Equal("Note ]] pad", Assert.Single(embeddedMarker.Directives).Arguments.GetProperty("query").GetString());
    }

    [Fact]
    public void Rejects_incomplete_nested_presentations()
    {
        var result = WorkspaceDirectiveParser.Parse(
            "No. [[workspace:{\"command\":\"application.open\",\"args\":{\"applicationId\":\"pc.application:notepad\",\"presentation\":{\"position\":{\"x\":0,\"y\":0,\"z\":0},\"rotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"size\":{\"x\":1,\"y\":1,\"z\":1},\"unexpected\":true}}]]");

        Assert.Empty(result.Directives);
    }

    [Fact]
    public void Profile_save_requires_its_structured_launch_policy()
    {
        var result = WorkspaceDirectiveParser.Parse(
            "No. [[workspace:{\"command\":\"application.profile.save\",\"args\":{\"id\":\"profile:notepad\",\"displayName\":\"Notepad\",\"applicationId\":\"pc.application:notepad\",\"arguments\":[]}}]]");

        Assert.Empty(result.Directives);
    }

    [Fact]
    public void Focus_uses_the_renderer_supported_exact_window_target()
    {
        var result = WorkspaceDirectiveParser.Parse(
            "Focusing it. [[workspace:{\"command\":\"window.focus\",\"args\":{\"windowEntityId\":\"pc.window:notepad\"}}]]");

        Assert.Equal("pc.window:notepad", Assert.Single(result.Directives).Arguments.GetProperty("windowEntityId").GetString());
    }

    [Fact]
    public void Rejects_shell_like_profile_arguments_and_host_invalid_geometry()
    {
        var shellArgument = WorkspaceDirectiveParser.Parse(
            "No. [[workspace:{\"command\":\"application.profile.save\",\"args\":{\"id\":\"profile:notepad\",\"displayName\":\"Notepad\",\"applicationId\":\"pc.application:notepad\",\"arguments\":[\"cmd.exe /c whoami\"],\"launchPolicy\":\"reuseOrLaunch\"}}]]");
        var tinyRotation = WorkspaceDirectiveParser.Parse(
            "No. [[workspace:{\"command\":\"application.open\",\"args\":{\"applicationId\":\"pc.application:notepad\",\"presentation\":{\"position\":{\"x\":0,\"y\":0,\"z\":0},\"rotation\":{\"x\":0.0000001,\"y\":0,\"z\":0,\"w\":0},\"size\":{\"x\":1,\"y\":1,\"z\":1}}}}]]");

        Assert.Empty(shellArgument.Directives);
        Assert.Empty(tinyRotation.Directives);
    }

    [Theory]
    [InlineData("shell.run")]
    [InlineData("application.launch")]
    public void Rejects_non_workspace_operations(string command)
    {
        var result = WorkspaceDirectiveParser.Parse(
            $"No. [[workspace:{{\"command\":\"{command}\",\"args\":{{}}}}]]");

        Assert.Equal("No.", result.SpokenText);
        Assert.Empty(result.Directives);
    }

    [Theory]
    [InlineData("{\"command\":\"application.open\",\"args\":{\"query\":\"Notepad\",\"executablePath\":\"C:\\\\bad.exe\"}}")]
    [InlineData("{\"command\":\"application.search\",\"args\":{\"query\":\"Notepad\"},\"danger\":true}")]
    [InlineData("{\"command\":\"application.open\",\"args\":{")]
    public void Rejects_untrusted_or_malformed_directive_content(string payload)
    {
        var result = WorkspaceDirectiveParser.Parse($"No. [[workspace:{payload}]]");

        Assert.Equal("No.", result.SpokenText);
        Assert.Empty(result.Directives);
    }

    [Fact]
    public void Rejects_duplicate_keys_and_invalid_target_smuggled_alongside_a_query()
    {
        var duplicate = WorkspaceDirectiveParser.Parse(
            "No. [[workspace:{\"command\":\"application.search\",\"command\":\"application.open\",\"args\":{\"query\":\"Notepad\"}}]]");
        var smuggledId = WorkspaceDirectiveParser.Parse(
            "No. [[workspace:{\"command\":\"application.open\",\"args\":{\"query\":\"Notepad\",\"applicationId\":\"not-an-app\"}}]]");

        Assert.Empty(duplicate.Directives);
        Assert.Empty(smuggledId.Directives);
    }

    [Fact]
    public void Rejects_directive_payload_over_64_kib()
    {
        var query = new string('x', WorkspaceDirectiveParser.MaximumDirectivePayloadBytes);
        var payload = JsonSerializer.Serialize(new { command = "application.search", args = new { query } });

        var result = WorkspaceDirectiveParser.Parse($"No. [[workspace:{payload}]]");

        Assert.Equal("No.", result.SpokenText);
        Assert.Empty(result.Directives);
    }

    [Theory]
    [InlineData("application.close", "{\"windowEntityId\":\"not-a-window\"}")]
    [InlineData("surface.bindWindow", "{\"surfaceEntityId\":\"pc.window:x\",\"windowEntityId\":\"spatial.surface:y\"}")]
    [InlineData("application.open", "{\"applicationId\":\"pc.application:notepad\",\"profileId\":\"profile:notepad\"}")]
    public void Rejects_invalid_operation_specific_ids(string command, string args)
    {
        var result = WorkspaceDirectiveParser.Parse(
            $"No. [[workspace:{{\"command\":\"{command}\",\"args\":{args}}}]]");

        Assert.Empty(result.Directives);
    }
}
