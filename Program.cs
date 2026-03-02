using Microsoft.EntityFrameworkCore;
using AgentControlPanel.Data;
using AgentControlPanel.Services;
using Amazon.BedrockRuntime;

var builder = WebApplication.CreateBuilder(args);
AgentControlPanel.Services.DocumentInspector.Inspect();

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddAWSService<IAmazonBedrockRuntime>();
builder.Services.AddScoped<IBedrockService, BedrockService>();
builder.Services.AddSingleton<ISkillParser, SkillParser>();
builder.Services.AddScoped<IConversationService, ConversationService>();
builder.Services.AddScoped<IQdrantService, QdrantService>();

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

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();
