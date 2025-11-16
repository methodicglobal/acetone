using Acetone.V2.Proxy.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;

namespace Acetone.V2.Proxy.Tests.Middleware;

public class ExceptionHandlingMiddlewareTests
{
    private readonly Mock<ILogger<ExceptionHandlingMiddleware>> _loggerMock;
    private readonly Mock<RequestDelegate> _nextMock;
    private readonly ExceptionHandlingMiddleware _middleware;

    public ExceptionHandlingMiddlewareTests()
    {
        _loggerMock = new Mock<ILogger<ExceptionHandlingMiddleware>>();
        _nextMock = new Mock<RequestDelegate>();
        _middleware = new ExceptionHandlingMiddleware(
            _nextMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task InvokeAsync_WithNoException_ShouldCallNext()
    {
        // Arrange
        var context = new DefaultHttpContext();
        _nextMock.Setup(n => n(context)).Returns(Task.CompletedTask);

        // Act
        await _middleware.InvokeAsync(context);

        // Assert
        _nextMock.Verify(n => n(context), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_WithException_ShouldReturn500()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        _nextMock.Setup(n => n(context)).ThrowsAsync(new Exception("Test error"));

        // Act
        await _middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(500);
    }

    [Fact]
    public async Task InvokeAsync_WithException_ShouldReturnJsonError()
    {
        // Arrange
        var context = new DefaultHttpContext();
        var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        _nextMock.Setup(n => n(context)).ThrowsAsync(new Exception("Test error"));

        // Act
        await _middleware.InvokeAsync(context);

        // Assert
        context.Response.ContentType.Should().Be("application/json");

        responseBody.Seek(0, SeekOrigin.Begin);
        var reader = new StreamReader(responseBody);
        var content = await reader.ReadToEndAsync();
        content.Should().NotBeNullOrEmpty();

        var errorResponse = JsonSerializer.Deserialize<Dictionary<string, object>>(content);
        errorResponse.Should().ContainKey("error");
    }

    [Fact]
    public async Task InvokeAsync_WithException_ShouldLogError()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var exception = new Exception("Test error");
        _nextMock.Setup(n => n(context)).ThrowsAsync(exception);

        // Act
        await _middleware.InvokeAsync(context);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                exception,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_WithException_ShouldIncludeCorrelationId()
    {
        // Arrange
        var context = new DefaultHttpContext();
        var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        var correlationId = "test-correlation-id";
        context.Request.Headers["X-Correlation-ID"] = correlationId;

        _nextMock.Setup(n => n(context)).ThrowsAsync(new Exception("Test error"));

        // Act
        await _middleware.InvokeAsync(context);

        // Assert
        context.Response.Headers.Should().ContainKey("X-Correlation-ID");
        context.Response.Headers["X-Correlation-ID"].ToString().Should().Be(correlationId);
    }

    [Fact]
    public async Task InvokeAsync_WithKeyNotFoundException_ShouldReturn404()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        _nextMock.Setup(n => n(context)).ThrowsAsync(new KeyNotFoundException("Not found"));

        // Act
        await _middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task InvokeAsync_WithTimeoutException_ShouldReturn504()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        _nextMock.Setup(n => n(context)).ThrowsAsync(new TimeoutException("Timeout"));

        // Act
        await _middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(504);
    }
}
