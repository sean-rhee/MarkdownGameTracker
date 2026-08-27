using MarkdownGameTracker.Api;
using MarkdownGameTracker.Services;
using MarkdownGameTracker.Storage;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info = new()
        {
            Title = "Markdown Game Tracker API",
            Version = "v1",
            Description = "CRUD API for game notes stored as Markdown and YAML frontmatter in an Obsidian vault."
        };
        return Task.CompletedTask;
    });
});
builder.Services
    .AddOptions<VaultOptions>()
    .Bind(builder.Configuration.GetSection(VaultOptions.SectionName))
    .Validate(options => !string.IsNullOrWhiteSpace(options.Path),
        "Vault:Path must point to the Obsidian vault.")
    .ValidateOnStart();
builder.Services
    .AddOptions<IgdbOptions>()
    .Bind(builder.Configuration.GetSection(IgdbOptions.SectionName));
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<IIgdbDescriptionService, IgdbDescriptionService>(client =>
    client.Timeout = TimeSpan.FromSeconds(12));
builder.Services.AddSingleton<IGameRepository, MarkdownGameRepository>();
builder.Services.AddSingleton<MarkdownRenderer>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
    app.UseHttpsRedirection();
}
else
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "Markdown Game Tracker API v1");
        options.DocumentTitle = "Markdown Game Tracker API";
    });
}

app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();
app.MapGameEndpoints();
app.MapGameMetadataEndpoints();
app.MapMarkdownEndpoints();

app.Run();

public partial class Program;
