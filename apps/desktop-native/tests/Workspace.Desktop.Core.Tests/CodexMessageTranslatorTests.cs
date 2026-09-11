using System.Text.Json;
using Workspace.Desktop.Core.Agent;

namespace Workspace.Desktop.Core.Tests;

public sealed class CodexMessageTranslatorTests
{
    private readonly CodexMessageTranslator _translator = new();

    [Fact]
    public void Translates_assistant_deltas()
    {
        var result = _translator.Translate("""
            {"method":"item/agentMessage/delta","params":{"threadId":"thread-1","turnId":"turn-1","itemId":"item-1","delta":"Workspace ready."}}
            """);

        var delta = Assert.IsType<AgentAssistantDelta>(result);
        Assert.Equal("Workspace ready.", delta.Text);
        Assert.Equal("turn-1", delta.TurnId);
    }

    [Fact]
    public void Translates_command_progress_without_leaking_credentials()
    {
        var result = _translator.Translate("""
            {"method":"item/commandExecution/outputDelta","params":{"threadId":"t","turnId":"v","itemId":"i","delta":"OPENAI_API_KEY=sk-secret PASSWORD=hunter2 build passed"}}
            """);

        var output = Assert.IsType<AgentTerminalDelta>(result);
        Assert.DoesNotContain("sk-secret", output.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", output.Text, StringComparison.Ordinal);
        Assert.Contains("build passed", output.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Translates_server_approval_requests_to_a_bounded_safe_summary()
    {
        var result = _translator.Translate("""
            {"id":42,"method":"item/commandExecution/requestApproval","params":{"threadId":"t","turnId":"v","itemId":"i","command":"$env:API_TOKEN='secret'; npm test","cwd":"C:\\repo","reason":"Run tests","environment":{"API_TOKEN":"secret"},"startedAtMs":1}}
            """);

        var approval = Assert.IsType<AgentApprovalRequested>(result);
        Assert.Equal("42", approval.RequestId);
        Assert.Equal("item/commandExecution/requestApproval", approval.Method);
        Assert.DoesNotContain("secret", approval.CommandSummary, StringComparison.Ordinal);
        Assert.DoesNotContain("environment", JsonSerializer.Serialize(approval), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Translates_turn_completion_and_protocol_failures()
    {
        var completed = _translator.Translate("""
            {"method":"turn/completed","params":{"threadId":"t","turn":{"id":"turn-1","status":"completed","items":[]}}}
            """);

        Assert.Equal("completed", Assert.IsType<AgentTurnCompleted>(completed).Status);
        Assert.IsType<AgentProtocolFailure>(_translator.Translate("{not json"));
        Assert.IsType<AgentProcessExited>(_translator.TranslateExit(17, "server stopped"));
    }
}
