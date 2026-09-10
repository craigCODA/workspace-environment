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
    [InlineData("application.open", "{\"applicationId\":\"app:notepad\",\"profileId\":\"profile:notepad\"}")]
    public void Rejects_invalid_operation_specific_ids(string command, string args)
    {
        var result = WorkspaceDirectiveParser.Parse(
            $"No. [[workspace:{{\"command\":\"{command}\",\"args\":{args}}}]]");

        Assert.Empty(result.Directives);
    }
}
