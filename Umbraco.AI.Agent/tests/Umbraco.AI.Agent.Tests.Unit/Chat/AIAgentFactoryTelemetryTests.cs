using System.Diagnostics;
using Microsoft.Extensions.AI;
using Moq;
using Shouldly;
using Umbraco.AI.Agent.Core.Agents;
using Umbraco.AI.Agent.Core.Chat;
using Umbraco.AI.Agent.Core.Surfaces;
using Umbraco.AI.Agent.Core.Workflows;
using Umbraco.AI.Core.Chat;
using Umbraco.AI.Core.Profiles;
using Umbraco.AI.Core.RuntimeContext;
using Umbraco.AI.Core.Telemetry;
using Umbraco.AI.Core.Tools;
using Umbraco.AI.Core.Tools.Scopes;
using Xunit;
using UmbracoAIAgent = Umbraco.AI.Agent.Core.Agents.AIAgent;

namespace Umbraco.AI.Agent.Tests.Unit.Chat;

/// <summary>
/// An agent run emits one OpenTelemetry <c>invoke_agent</c> span under the Umbraco.AI source, carrying the agent's
/// name and a stable ID, which is what the Azure portal's Agents views are built from.
/// </summary>
public class AIAgentFactoryTelemetryTests
{
    private static readonly Guid ProfileId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task RunAsync_EmitsAnInvokeAgentSpanWithTheAgentsNameAndStableId()
    {
        var definition = CreateDefinition();
        var factory = CreateFactory();
        var spans = new List<Activity>();
        using var listener = Listen(spans);

        var agent = await factory.CreateAgentAsync(definition);
        await agent.RunAsync([new ChatMessage(ChatRole.User, "hi")]);

        var span = spans.Single(a => a.GetTagItem("gen_ai.operation.name") as string == "invoke_agent");
        span.GetTagItem("gen_ai.agent.name").ShouldBe("Test Agent");
        span.GetTagItem("gen_ai.agent.id").ShouldBe(definition.Id.ToString("N"));
    }

    [Fact]
    public async Task CreateAgentAsync_GivesTheSameAgentTheSameIdEveryTime()
    {
        var definition = CreateDefinition();
        var factory = CreateFactory();

        var first = await factory.CreateAgentAsync(definition);
        var second = await factory.CreateAgentAsync(definition);

        first.Id.ShouldBe(definition.Id.ToString("N"));
        second.Id.ShouldBe(first.Id);
    }

    private static ActivityListener Listen(List<Activity> spans)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == AITelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = spans.Add,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private static UmbracoAIAgent CreateDefinition()
        => new()
        {
            Id = Guid.NewGuid(),
            Alias = "test-agent",
            Name = "Test Agent",
            ProfileId = ProfileId,
            AgentType = AIAgentType.Standard,
            Config = new AIStandardAgentConfig { AllowedToolIds = [], AllowedToolScopeIds = [] },
            IsActive = true,
        };

    private static AIAgentFactory CreateFactory()
    {
        var chatClientMock = new Mock<IChatClient>();
        chatClientMock
            .Setup(x => x.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));

        var profile = new AIProfile { Id = ProfileId, Alias = "test", Name = "Test", ConnectionId = Guid.Empty };

        var profileServiceMock = new Mock<IAIProfileService>();
        profileServiceMock
            .Setup(x => x.GetProfileAsync(ProfileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);

        var chatClientFactoryMock = new Mock<IAIChatClientFactory>();
        chatClientFactoryMock
            .Setup(x => x.CreateClientAsync(profile, It.IsAny<CancellationToken>()))
            .ReturnsAsync(chatClientMock.Object);

        var functionFactoryMock = new Mock<IAIFunctionFactory>();
        functionFactoryMock
            .Setup(f => f.Create(It.IsAny<IEnumerable<IAITool>>()))
            .Returns(new List<AIFunction>());

        return new AIAgentFactory(
            new AIRuntimeContextScopeProvider(),
            new AIRuntimeContextContributorCollection(() => []),
            profileServiceMock.Object,
            chatClientFactoryMock.Object,
            new AIToolCollection(() => []),
            new AIToolScopeCollection(() => []),
            functionFactoryMock.Object,
            new AIAgentWorkflowCollection(() => []),
            new AIAgentSurfaceCollection(() => []));
    }
}
