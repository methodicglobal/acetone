using Acetone.V2.Core.Configuration;
using Microsoft.Extensions.Options;

namespace Acetone.V2.Core.Tests.Configuration;

public class AcetoneOptionsValidatorTests
{
    private readonly AcetoneOptionsValidator _validator;

    public AcetoneOptionsValidatorTests()
    {
        _validator = new AcetoneOptionsValidator();
    }

    [Fact]
    public void Validate_ValidOptions_ReturnsSuccess()
    {
        // Arrange
        var options = new AcetoneOptions
        {
            MaxConcurrentRequests = 50,
            EnableDetailedLogging = true
        };

        // Act
        var result = _validator.Validate("Acetone", options);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_NullOptions_ReturnsFailed()
    {
        // Act
        var result = _validator.Validate("Acetone", null!);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains("cannot be null", result.FailureMessage);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1001)]
    [InlineData(5000)]
    public void Validate_MaxConcurrentRequestsOutOfRange_ReturnsFailed(int value)
    {
        // Arrange
        var options = new AcetoneOptions { MaxConcurrentRequests = value };

        // Act
        var result = _validator.Validate("Acetone", options);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains("MaxConcurrentRequests", result.FailureMessage);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(100)]
    [InlineData(1000)]
    public void Validate_MaxConcurrentRequestsInRange_ReturnsSuccess(int value)
    {
        // Arrange
        var options = new AcetoneOptions { MaxConcurrentRequests = value };

        // Act
        var result = _validator.Validate("Acetone", options);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_ResilienceIsNull_ReturnsFailed()
    {
        // Arrange
        var options = new AcetoneOptions { Resilience = null! };

        // Act
        var result = _validator.Validate("Acetone", options);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains("Resilience", result.FailureMessage);
    }
}
