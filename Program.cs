using Jarvis.Mind.Api.Services;
using Jarvis.Mind.Api.Services.Regions;
using Jarvis.Mind.Api.Services.Stores;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Stores. Swap NullEmbeddingStore -> ChromaEmbeddingStore once you've run the embedding
// generation job and Jarvis:ChromaCollection actually has vectors in it.
builder.Services.AddScoped<IMemoryStore, SqliteMemoryStore>();
builder.Services.AddScoped<IEmbeddingStore, NullEmbeddingStore>();
builder.Services.AddHttpClient<ChromaEmbeddingStore>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Jarvis:ChromaBaseUrl"] ?? "http://localhost:8000");
});

// Honest empty stubs so the endpoint runs today. Swap each for your real orchestrator
// registry / tool registry / knowledge manifest / conversation store one at a time -
// the assembler and frontend don't change when you do.
builder.Services.AddScoped<IAgentRegistry, EmptyAgentRegistry>();
builder.Services.AddScoped<IToolRegistry, EmptyToolRegistry>();
builder.Services.AddScoped<IKnowledgeManifest, EmptyKnowledgeManifest>();
builder.Services.AddScoped<IConversationRepo, EmptyConversationRepo>();

builder.Services.AddScoped<IMindMapAssembler, MindMapAssembler>();
builder.Services.AddScoped<INodeDetailService, NodeDetailService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.MapControllers();

// Tier 2+ will add: app.UseWebSockets(); app.Map("/ws/observe", ...);
// Tier 2+ will add: static file hosting for the built Vue app under /jarvis.

app.Run();