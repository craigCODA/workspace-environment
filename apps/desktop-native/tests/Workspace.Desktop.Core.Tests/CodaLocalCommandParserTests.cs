using Workspace.Desktop.Core.Runtime;

namespace Workspace.Desktop.Core.Tests;

public sealed class CodaLocalCommandParserTests
{
    [Theory]
    [InlineData("stop", CodaLocalCommandKind.Stop)]
    [InlineData("show terminal", CodaLocalCommandKind.ShowTerminal)]
    [InlineData("hide the terminal", CodaLocalCommandKind.HideTerminal)]
    [InlineData("turn captions off", CodaLocalCommandKind.CaptionsOff)]
    [InlineData("keep critical alerts only", CodaLocalCommandKind.ProactiveCritical)]
    [InlineData("go home", CodaLocalCommandKind.ReturnHome)]
    public void Recognizes_local_control_vocabulary(string text, CodaLocalCommandKind expected)
    {
        Assert.Equal(expected, CodaLocalCommandParser.Parse(text).Kind);
    }

    [Fact]
    public void Extracts_a_new_preferred_name_without_sending_it_to_codex()
    {
        var command = CodaLocalCommandParser.Parse("Call me Morgan");

        Assert.Equal(CodaLocalCommandKind.ChangeName, command.Kind);
        Assert.Equal("Morgan", command.Argument);
    }

    [Fact]
    public void General_requests_are_left_for_the_coding_agent()
    {
        Assert.Equal(CodaLocalCommandKind.AgentRequest,
            CodaLocalCommandParser.Parse("build a floating project browser").Kind);
    }

    [Theory]
    [InlineData("open Notepad here")]
    [InlineData("restart this app")]
    [InlineData("save this as PythOS Codex")]
    public void Application_requests_are_left_for_the_typed_agent_path(string text)
    {
        var command = CodaLocalCommandParser.Parse(text);

        Assert.Equal(CodaLocalCommandKind.AgentRequest, command.Kind);
        Assert.Equal(text, command.Argument);
    }

    [Theory]
    [InlineData("allow once")]
    [InlineData("remember this")]
    [InlineData("deny")]
    public void Approval_words_are_not_global_local_commands(string text)
    {
        Assert.Equal(CodaLocalCommandKind.AgentRequest, CodaLocalCommandParser.Parse(text).Kind);
    }
}
