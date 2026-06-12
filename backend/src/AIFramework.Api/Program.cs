using AIFramework.Api;
using AIFramework.Core.Agents;
using AIFramework.Core.Agents.Tools;
using AIFramework.Core.Llm;
using AIFramework.Core.Observability;
using AIFramework.Core.Prompts;
using AIFramework.Core.Rag;
using AIFramework.Core.Rag.Embeddings;
using AIFramework.Core.Rag.VectorStore;

var builder = WebApplication.CreateBuilder(args);

// ----- Composition root: the only place that knows which vendor is in use. -----

var llmOptions = builder.Configuration.GetSection("Llm").Get<LlmOptions>() ?? new LlmOptions();
var embeddingOptions = builder.Configuration.GetSection("Embeddings").Get<EmbeddingOptions>() ?? new EmbeddingOptions();

builder.Services.AddSingleton<UsageTracker>();
builder.Services.AddSingleton<ILlmProvider>(services => new InstrumentedLlmProvider(
    LlmProviderFactory.Create(llmOptions),
    services.GetRequiredService<UsageTracker>()));

builder.Services.AddSingleton(new ToolRegistry(new CalculatorTool(), new ClockTool()));
builder.Services.AddSingleton(services => new Agent(
    services.GetRequiredService<ILlmProvider>(),
    services.GetRequiredService<ToolRegistry>()));

builder.Services.AddSingleton<IVectorStore, InMemoryVectorStore>();
builder.Services.AddSingleton(EmbeddingProviderFactory.Create(embeddingOptions));
builder.Services.AddSingleton(services => new RagPipeline(
    services.GetRequiredService<ILlmProvider>(),
    services.GetRequiredService<IEmbeddingProvider>(),
    services.GetRequiredService<IVectorStore>()));

// Prompts live in /prompts at the repo root so they're shared with evals and reviewed in PRs.
var promptsDirectory = builder.Configuration["PromptsDirectory"]
    ?? Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "..", "..", "prompts"));
builder.Services.AddSingleton(Directory.Exists(promptsDirectory)
    ? PromptRegistry.LoadFromDirectory(promptsDirectory)
    : new PromptRegistry());

builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(builder.Configuration["FrontendOrigin"] ?? "http://localhost:5173")
        .AllowAnyHeader()
        .AllowAnyMethod()));

var app = builder.Build();
app.UseCors();

app.MapGet("/api/health", (ILlmProvider provider) => Results.Ok(new
{
    status = "ok",
    provider = provider.Name,
    model = provider.DefaultModel,
}));

app.MapChatEndpoints();
app.MapAgentEndpoints();
app.MapRagEndpoints();

app.MapGet("/api/usage", (UsageTracker tracker) => Results.Ok(tracker.GetReport()));

app.MapGet("/api/prompts", (PromptRegistry prompts) => Results.Ok(
    prompts.All().Select(p => new { p.Name, p.Version, p.Description, p.Template.Variables })));

app.Run();
