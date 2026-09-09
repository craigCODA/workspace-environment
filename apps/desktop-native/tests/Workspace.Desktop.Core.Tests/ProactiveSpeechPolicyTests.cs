using Workspace.Desktop.Core.Preferences;
using Workspace.Desktop.Core.Runtime;

namespace Workspace.Desktop.Core.Tests;

public sealed class ProactiveSpeechPolicyTests
{
    [Theory]
    [InlineData(ProactiveEventKind.Critical, true)]
    [InlineData(ProactiveEventKind.Failure, true)]
    [InlineData(ProactiveEventKind.Risk, true)]
    [InlineData(ProactiveEventKind.Blocked, true)]
    [InlineData(ProactiveEventKind.Completion, false)]
    [InlineData(ProactiveEventKind.Information, false)]
    public void Critical_only_announces_only_important_interruptions(
        ProactiveEventKind kind,
        bool expected)
    {
        Assert.Equal(expected, ProactiveSpeechPolicy.ShouldSpeak(
            ProactiveSpeechMode.CriticalOnly,
            kind));
    }

    [Fact]
    public void Include_completion_adds_success_without_enabling_general_information()
    {
        Assert.True(ProactiveSpeechPolicy.ShouldSpeak(
            ProactiveSpeechMode.IncludeCompletion,
            ProactiveEventKind.Completion));
        Assert.False(ProactiveSpeechPolicy.ShouldSpeak(
            ProactiveSpeechMode.IncludeCompletion,
            ProactiveEventKind.Information));
    }

    [Fact]
    public void Quiet_never_speaks_proactively()
    {
        foreach (var kind in Enum.GetValues<ProactiveEventKind>())
        {
            Assert.False(ProactiveSpeechPolicy.ShouldSpeak(ProactiveSpeechMode.Quiet, kind));
        }
    }

    [Fact]
    public void Custom_mode_matches_exact_enabled_events()
    {
        var enabled = new[] { ProactiveEventKind.Blocked, ProactiveEventKind.Completion };

        Assert.True(ProactiveSpeechPolicy.ShouldSpeak(
            ProactiveSpeechMode.Custom,
            ProactiveEventKind.Blocked,
            enabled));
        Assert.True(ProactiveSpeechPolicy.ShouldSpeak(
            ProactiveSpeechMode.Custom,
            ProactiveEventKind.Completion,
            enabled));
        Assert.False(ProactiveSpeechPolicy.ShouldSpeak(
            ProactiveSpeechMode.Custom,
            ProactiveEventKind.Critical,
            enabled));
    }
}
