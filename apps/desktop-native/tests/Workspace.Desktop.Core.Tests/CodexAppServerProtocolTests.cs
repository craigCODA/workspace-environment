using System.Text.Json;
using Workspace.Desktop.Core.Agent;

namespace Workspace.Desktop.Core.Tests;

public sealed class CodexAppServerProtocolTests
{
    [Fact]
    public void Initialize_identifies_the_native_workspace_client_without_credentials()
    {
        using var document = JsonDocument.Parse(CodexProtocolMessages.Initialize(1));
        var root = document.RootElement;

        Assert.Equal(1, root.GetProperty("id").GetInt64());
        Assert.Equal("initialize", root.GetProperty("method").GetString());
        Assert.Equal("workspace_environment",
            root.GetProperty("params").GetProperty("clientInfo").GetProperty("name").GetString());
        Assert.DoesNotContain("apiKey", root.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OPENAI_API_KEY", root.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"env\"", root.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Thread_start_is_scoped_to_the_selected_root_and_requested_sandbox()
    {
        using var document = JsonDocument.Parse(CodexProtocolMessages.StartThread(
            2,
            @"C:\work\scene",
            AgentSandbox.ReadOnly));
        var parameters = document.RootElement.GetProperty("params");

        Assert.Equal(@"C:\work\scene", parameters.GetProperty("cwd").GetString());
        Assert.Equal("on-request", parameters.GetProperty("approvalPolicy").GetString());
        Assert.Equal("read-only", parameters.GetProperty("sandbox").GetString());
    }

    [Fact]
    public void Thread_start_and_resume_pin_Coda_to_gpt_5_5_with_high_reasoning()
    {
        using var start = JsonDocument.Parse(CodexProtocolMessages.StartThread(
            3,
            @"C:\work\scene",
            AgentSandbox.ReadOnly));
        using var resume = JsonDocument.Parse(CodexProtocolMessages.ResumeThread(
            4,
            "thread-1",
            @"C:\work\scene",
            AgentSandbox.ReadOnly));

        foreach (var parameters in new[]
                 {
                     start.RootElement.GetProperty("params"),
                     resume.RootElement.GetProperty("params"),
                 })
        {
            Assert.Equal("gpt-5.5", parameters.GetProperty("model").GetString());
            Assert.Equal("high", parameters.GetProperty("config")
                .GetProperty("model_reasoning_effort").GetString());
        }
    }

    [Fact]
    public void Turn_start_and_steer_use_text_input_arrays()
    {
        using var start = JsonDocument.Parse(CodexProtocolMessages.StartTurn(
            3,
            "thread-1",
            "Inspect the scene."));
        using var steer = JsonDocument.Parse(CodexProtocolMessages.SteerTurn(
            4,
            "thread-1",
            "turn-1",
            "Stop after tests."));

        Assert.Equal("text", start.RootElement.GetProperty("params")
            .GetProperty("input")[0].GetProperty("type").GetString());
        Assert.Equal("turn-1", steer.RootElement.GetProperty("params")
            .GetProperty("expectedTurnId").GetString());
    }

    [Fact]
    public void Approval_response_uses_the_original_server_request_id()
    {
        using var response = JsonDocument.Parse(CodexProtocolMessages.ApprovalResponse(
            "42",
            AgentApprovalDecision.AcceptForSession));

        Assert.Equal(42, response.RootElement.GetProperty("id").GetInt64());
        Assert.Equal("acceptForSession",
            response.RootElement.GetProperty("result").GetProperty("decision").GetString());
    }
}
