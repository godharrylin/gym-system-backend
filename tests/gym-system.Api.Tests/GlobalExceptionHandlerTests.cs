using System.Text.Json;
using gym_system.Api.Errors;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace gym_system.Api.Tests;

public sealed class GlobalExceptionHandlerTests
{
    [Fact]
    public async Task UnexpectedException_ShouldReturnSafeContractWithoutInternalDetails()
    {
        var handler = new GlobalExceptionHandler(
            NullLogger<GlobalExceptionHandler>.Instance,
            new ProductionEnvironment());
        var context = new DefaultHttpContext
        {
            TraceIdentifier = "registration-trace-123"
        };
        context.Response.Body = new MemoryStream();

        var handled = await handler.TryHandleAsync(
            context,
            new Exception("Server=secret;Password=secret; SQL failed"),
            default);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var json = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal("INTERNAL_SERVER_ERROR", json.RootElement.GetProperty("code").GetString());
        Assert.Equal("registration-trace-123", json.RootElement.GetProperty("traceId").GetString());
        var responseText = json.RootElement.GetRawText();
        Assert.DoesNotContain("Password", responseText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SQL failed", responseText, StringComparison.OrdinalIgnoreCase);
        Assert.False(json.RootElement.TryGetProperty("exceptionType", out _));
    }

    private sealed class ProductionEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "gym-system.Api.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
