using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using AgentControlPanel.Data;
using AgentControlPanel.Services;
using AgentControlPanel.Services.Embeddings;
using AgentControlPanel.Services.Llm;
using Amazon.BedrockRuntime;

// Load .env (if present) into environment variables BEFORE the host is built,
// so both ASP.NET config (keys like Voyage__ApiKey) and the providers' direct
// env-var fallbacks (ANTHROPIC_API_KEY / VOYAGE_API_KEY) can see them.
if (File.Exists(".env"))
{
    DotNetEnv.Env.Load();
}

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

// Rate limit only the expensive LLM / embedding API calls — NOT page navigation.
// The "llm" policy is a single shared fixed window (5 requests/minute total,
// regardless of caller). Apply it with [EnableRateLimiting("llm")] on the
// specific actions that call Claude or Voyage.
const string LlmRateLimitPolicy = "llm";
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter(LlmRateLimitPolicy, opt =>
    {
        opt.PermitLimit = 5;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        opt.QueueLimit = 0;
    });
});
// Build an Npgsql data source with pgvector support enabled, so the EF Core
// model can map the `vector` type used by the knowledge base embeddings.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
dataSourceBuilder.UseVector();
var dataSource = dataSourceBuilder.Build();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(dataSource, npgsql => npgsql.UseVector()));

// LLM provider configuration. Default is the direct Anthropic Claude SDK;
// set "Llm:Provider" to "Bedrock" to route through AWS Bedrock instead.
var llmOptions = builder.Configuration.GetSection("Llm").Get<LlmOptions>() ?? new LlmOptions();
builder.Services.AddSingleton(llmOptions);

if (string.Equals(llmOptions.Provider, "Bedrock", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddAWSService<IAmazonBedrockRuntime>();
    builder.Services.AddSingleton<ILlmProvider, BedrockLlmProvider>();
}
else
{
    builder.Services.AddSingleton<ILlmProvider, AnthropicLlmProvider>();
}

// Voyage AI embeddings (Anthropic's recommended embeddings partner — the
// Anthropic SDK has no embeddings API). Used to embed knowledge base entries.
var voyageOptions = builder.Configuration.GetSection("Voyage").Get<VoyageOptions>() ?? new VoyageOptions();
builder.Services.AddSingleton(voyageOptions);
builder.Services.AddHttpClient<IEmbeddingProvider, VoyageEmbeddingProvider>();

builder.Services.AddSingleton<ISkillParser, SkillParser>();
builder.Services.AddScoped<IKnowledgeBaseService, KnowledgeBaseService>();
builder.Services.AddScoped<IConversationService, ConversationService>();

var app = builder.Build();

// Apply migrations automatically
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseRateLimiter();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();
