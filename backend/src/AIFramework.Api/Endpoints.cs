using System.Text;
using System.Text.Json;
using AIFramework.Core.Agents;
using AIFramework.Core.Llm;
using AIFramework.Core.Rag;

namespace AIFramework.Api;

// ----- Request/response DTOs (the HTTP contract, kept separate from core types) -----

public sealed record ChatMessageDto(string Role, string Content)
{
    public ChatMessage ToCore() => Role.ToLowerInvariant() switch
    {
        "user" => ChatMessage.User(Content),
        "assistant" => ChatMessage.Assistant(Content),
        _ => throw new ArgumentException($"Unsupported role '{Role}' — use 'user' or 'assistant'."),
    };
}

public sealed record ChatRequestDto(List<ChatMessageDto> Messages, string? Model = null, string? SystemPrompt = null);

public sealed record AgentRequestDto(string Message);

public sealed record IngestRequestDto(string DocumentId, string Text);

public sealed record AskRequestDto(string Question);

public static class Endpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void MapChatEndpoints(this WebApplication app)
    {
        app.MapPost("/api/chat", async (ChatRequestDto request, ILlmProvider provider, CancellationToken ct) =>
        {
            var response = await provider.CompleteAsync(new ChatRequest
            {
                Model = request.Model,
                SystemPrompt = request.SystemPrompt,
                Messages = request.Messages.Select(m => m.ToCore()).ToList(),
            }, ct);

            return Results.Ok(new
            {
                text = response.Text,
                model = response.Model,
                finishReason = response.FinishReason.ToString(),
                usage = response.Usage,
            });
        });

        // Server-Sent Events: "delta" events carry text chunks, one final "done" event carries usage.
        app.MapPost("/api/chat/stream", async (ChatRequestDto request, ILlmProvider provider, HttpContext http, CancellationToken ct) =>
        {
            http.Response.ContentType = "text/event-stream";
            http.Response.Headers.CacheControl = "no-cache";

            await foreach (var streamEvent in provider.StreamAsync(new ChatRequest
            {
                Model = request.Model,
                SystemPrompt = request.SystemPrompt,
                Messages = request.Messages.Select(m => m.ToCore()).ToList(),
            }, ct))
            {
                var (eventName, payload) = streamEvent switch
                {
                    StreamEvent.TextDelta delta => ("delta", (object)new { text = delta.Text }),
                    StreamEvent.Completed completed => ("done", new
                    {
                        model = completed.Response.Model,
                        finishReason = completed.Response.FinishReason.ToString(),
                        usage = completed.Response.Usage,
                    }),
                    _ => throw new InvalidOperationException(),
                };
                await WriteSseAsync(http.Response, eventName, payload, ct);
            }
        });
    }

    public static void MapAgentEndpoints(this WebApplication app)
    {
        app.MapPost("/api/agent", async (AgentRequestDto request, Agent agent, CancellationToken ct) =>
        {
            var result = await agent.RunAsync(request.Message, ct);
            return Results.Ok(new
            {
                answer = result.Answer,
                usage = result.TotalUsage,
                steps = result.Steps.Select(step => step switch
                {
                    AgentStep.ToolExecution tool => new
                    {
                        type = "tool",
                        tool = (string?)tool.ToolName,
                        arguments = (string?)tool.Arguments,
                        result = tool.Result,
                    },
                    AgentStep.FinalAnswer final => new
                    {
                        type = "answer",
                        tool = (string?)null,
                        arguments = (string?)null,
                        result = final.Text,
                    },
                    _ => throw new InvalidOperationException(),
                }),
            });
        });
    }

    public static void MapRagEndpoints(this WebApplication app)
    {
        app.MapPost("/api/rag/documents", async (IngestRequestDto request, RagPipeline rag, CancellationToken ct) =>
        {
            var chunks = await rag.IngestAsync(request.DocumentId, request.Text, cancellationToken: ct);
            return Results.Ok(new { documentId = request.DocumentId, chunks });
        });

        app.MapPost("/api/rag/ask", async (AskRequestDto request, RagPipeline rag, CancellationToken ct) =>
        {
            var answer = await rag.AskAsync(request.Question, ct);
            return Results.Ok(new
            {
                answer = answer.Answer,
                usage = answer.Usage,
                sources = answer.Sources.Select((source, i) => new
                {
                    reference = i + 1,
                    documentId = source.Chunk.DocumentId,
                    score = source.Score,
                    excerpt = source.Chunk.Text.Length <= 300 ? source.Chunk.Text : source.Chunk.Text[..300] + "…",
                }),
            });
        });
    }

    private static async Task WriteSseAsync(HttpResponse response, string eventName, object payload, CancellationToken ct)
    {
        var message = $"event: {eventName}\ndata: {JsonSerializer.Serialize(payload, JsonOptions)}\n\n";
        await response.Body.WriteAsync(Encoding.UTF8.GetBytes(message), ct);
        await response.Body.FlushAsync(ct);
    }
}
