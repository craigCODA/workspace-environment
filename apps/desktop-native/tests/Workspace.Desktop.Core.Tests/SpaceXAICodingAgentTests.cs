using System.Net;
using System.Text;
using Workspace.Desktop.Core.Agent;

namespace Workspace.Desktop.Core.Tests;

public sealed class SpaceXAICodingAgentTests
{
    [Fact]
    public async Task StartAsync_requires_an_api_key()
    {
        await using var agent = new SpaceXAICodingAgent(apiKeyProvider: () => null);
        var eventsTask = ReadUntilAsync(
            agent,
            static item => item is AgentAuthenticationRequired,
            TimeSpan.FromSeconds(2));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => agent.StartAsync());

        Assert.Contains("XAI_API_KEY", error.Message, StringComparison.Ordinal);
        var events = await eventsTask;
        Assert.Contains(events, item => item is AgentAuthenticationRequired);
    }

    [Fact]
    public async Task StartTurnAsync_emits_assistant_text_from_chat_completions()
    {
        var handler = new StubHandler("""
            {"choices":[{"message":{"role":"assistant","content":"Hello from SpaceXAI"}}]}
            """);
        await using var agent = new SpaceXAICodingAgent(
            handler,
            apiKeyProvider: () => "test-key",
            baseAddress: new Uri("https://api.x.ai/v1/"));
        var eventsTask = ReadUntilAsync(
            agent,
            static item => item is AgentTurnCompleted,
            TimeSpan.FromSeconds(2));

        await agent.StartAsync();
        await agent.StartOrResumeThreadAsync(@"C:\workspace", null, AgentSandbox.ReadOnly);
        await agent.StartTurnAsync("Say hello");

        var events = await eventsTask;
        Assert.Contains(events, item => item is AgentAssistantDelta delta
            && delta.Text == "Hello from SpaceXAI");
        Assert.Contains(events, item => item is AgentTurnCompleted completed
            && completed.Status == "completed");
        Assert.Equal("Bearer test-key", handler.Authorization);
        Assert.Contains("\"model\":\"grok-4.6\"", handler.LastRequestBody, StringComparison.Ordinal);
    }

    private static async Task<List<AgentEvent>> ReadUntilAsync(
        ICodingAgent agent,
        Func<AgentEvent, bool> stopWhen,
        TimeSpan timeout)
    {
        var events = new List<AgentEvent>();
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await foreach (var item in agent.ReadEventsAsync(cts.Token))
            {
                events.Add(item);
                if (stopWhen(item))
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Timed out; return whatever was observed.
        }

        return events;
    }

    private sealed class StubHandler(string responseBody) : HttpMessageHandler
    {
        public string? Authorization { get; private set; }
        public string LastRequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Authorization = request.Headers.Authorization?.ToString();
            LastRequestBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            };
        }
    }
}
