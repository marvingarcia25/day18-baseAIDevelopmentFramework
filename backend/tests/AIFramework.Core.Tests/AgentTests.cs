using System.Text.Json;
using AIFramework.Core.Agents;
using AIFramework.Core.Agents.Tools;
using AIFramework.Core.Llm;

namespace AIFramework.Core.Tests;

public class AgentTests
{
    [Fact]
    public async Task Agent_answers_directly_when_no_tool_is_needed()
    {
        var provider = new FakeLlmProvider(FakeLlmProvider.TextResponse("Hello!"));
        var agent = new Agent(provider, new ToolRegistry(new CalculatorTool()));

        var result = await agent.RunAsync("Hi");

        Assert.Equal("Hello!", result.Answer);
        Assert.Single(result.Steps);
    }

    [Fact]
    public async Task Agent_executes_tool_and_feeds_result_back()
    {
        var arguments = JsonSerializer.SerializeToElement(new { expression = "2+3" });
        var provider = new FakeLlmProvider(
            FakeLlmProvider.ToolCallResponse(new ToolCall("call-1", "calculator", arguments)),
            FakeLlmProvider.TextResponse("The answer is 5."));
        var agent = new Agent(provider, new ToolRegistry(new CalculatorTool()));

        var result = await agent.RunAsync("What is 2+3?");

        Assert.Equal("The answer is 5.", result.Answer);
        var toolStep = Assert.IsType<AgentStep.ToolExecution>(result.Steps[0]);
        Assert.Equal("calculator", toolStep.ToolName);
        Assert.Equal("5", toolStep.Result);

        // The second request must contain the assistant tool-call turn and the tool result.
        var secondRequest = provider.Requests[1];
        Assert.Contains(secondRequest.Messages, m => m.Role == ChatRole.Assistant && m.ToolCalls is { Count: 1 });
        Assert.Contains(secondRequest.Messages, m => m.Role == ChatRole.Tool && m.Content == "5");
    }

    [Fact]
    public async Task Agent_reports_unknown_tool_back_to_the_model()
    {
        var arguments = JsonSerializer.SerializeToElement(new { });
        var provider = new FakeLlmProvider(
            FakeLlmProvider.ToolCallResponse(new ToolCall("call-1", "no_such_tool", arguments)),
            FakeLlmProvider.TextResponse("done"));
        var agent = new Agent(provider, new ToolRegistry());

        var result = await agent.RunAsync("Use a tool");

        var toolStep = Assert.IsType<AgentStep.ToolExecution>(result.Steps[0]);
        Assert.StartsWith("Error: unknown tool", toolStep.Result);
        Assert.Equal("done", result.Answer);
    }

    [Fact]
    public async Task Agent_stops_at_max_iterations()
    {
        var arguments = JsonSerializer.SerializeToElement(new { expression = "1+1" });
        var looping = Enumerable.Range(0, 3)
            .Select(_ => FakeLlmProvider.ToolCallResponse(new ToolCall("call", "calculator", arguments)))
            .ToArray();
        var provider = new FakeLlmProvider(looping);
        var agent = new Agent(provider, new ToolRegistry(new CalculatorTool()),
            new AgentOptions { MaxIterations = 3 });

        var result = await agent.RunAsync("loop forever");

        Assert.Contains("Stopped after 3 iterations", result.Answer);
        Assert.Equal(3, provider.Requests.Count);
    }
}

public class ToolTests
{
    [Theory]
    [InlineData("2+3", "5")]
    [InlineData("(12.5 * 4) / 3", "16.6666666666667")]
    [InlineData("10 % 3", "1")]
    public async Task Calculator_evaluates_expressions(string expression, string expected)
    {
        var tool = new CalculatorTool();
        var arguments = JsonSerializer.SerializeToElement(new { expression });

        Assert.Equal(expected, await tool.ExecuteAsync(arguments));
    }

    [Fact]
    public async Task Calculator_rejects_non_arithmetic_input()
    {
        var tool = new CalculatorTool();
        var arguments = JsonSerializer.SerializeToElement(new { expression = "DROP TABLE users" });

        Assert.StartsWith("Error", await tool.ExecuteAsync(arguments));
    }
}
