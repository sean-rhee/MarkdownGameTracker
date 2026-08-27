using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MarkdownGameTracker.Models;
using MarkdownGameTracker.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace MarkdownGameTracker.Tests;


public sealed class OpenApiTests
{
    [Fact]
    public async Task OpenApi_document_and_swagger_ui_describe_the_crud_api()
    {
        using var app = new TestApp();

        var openApiResponse = await app.Client.GetAsync("/openapi/v1.json");
        var swaggerResponse = await app.Client.GetAsync("/swagger/index.html");

        Assert.Equal(HttpStatusCode.OK, openApiResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, swaggerResponse.StatusCode);

        using var document = JsonDocument.Parse(await openApiResponse.Content.ReadAsStreamAsync());
        Assert.Equal("Markdown Game Tracker API", document.RootElement.GetProperty("info").GetProperty("title").GetString());
        var paths = document.RootElement.GetProperty("paths");
        Assert.Equal("List game notes", paths.GetProperty("/api/games").GetProperty("get").GetProperty("summary").GetString());
        Assert.True(paths.GetProperty("/api/games").GetProperty("post").TryGetProperty("requestBody", out _));
        Assert.True(paths.GetProperty("/api/games/{id}").TryGetProperty("get", out _));
        Assert.True(paths.GetProperty("/api/games/{id}").TryGetProperty("put", out _));
        Assert.True(paths.GetProperty("/api/games/{id}").TryGetProperty("delete", out _));
        Assert.True(paths.GetProperty("/api/games/{id}/igdb-description").TryGetProperty("get", out _));
        Assert.True(paths.GetProperty("/api/games/{id}/igdb-matches").TryGetProperty("get", out _));
        Assert.True(paths.GetProperty("/api/games/igdb-title-suggestions").TryGetProperty("get", out _));
        Assert.True(paths.GetProperty("/api/games/igdb-card-artwork").TryGetProperty("post", out _));
        Assert.True(paths.GetProperty("/api/markdown/preview").TryGetProperty("post", out _));

        var swaggerHtml = await swaggerResponse.Content.ReadAsStringAsync();
        Assert.Contains("id=\"swagger-ui\"", swaggerHtml, StringComparison.OrdinalIgnoreCase);
    }
}
