using System.ComponentModel.DataAnnotations;
using Acetone.V2.Core.Configuration;

namespace Acetone.V2.Core.Tests.Configuration;

public class AcetoneOptionsTests
{
    [Fact]
    public void AcetoneOptions_DefaultValues_AreSet()
    {
        // Arrange & Act
        var options = new AcetoneOptions();

        // Assert
        Assert.NotNull(options.Resilience);
        Assert.False(options.EnableDetailedLogging);
        Assert.Equal(100, options.MaxConcurrentRequests);
        Assert.Null(options.ServiceFabricConnectionString);
    }

    [Fact]
    public void AcetoneOptions_SectionName_IsCorrect()
    {
        // Assert
        Assert.Equal("Acetone", AcetoneOptions.SectionName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1001)]
    public void AcetoneOptions_MaxConcurrentRequests_OutOfRange_FailsValidation(int value)
    {
        // Arrange
        var options = new AcetoneOptions { MaxConcurrentRequests = value };
        var context = new ValidationContext(options);
        var results = new List<ValidationResult>();

        // Act
        var isValid = Validator.TryValidateObject(options, context, results, validateAllProperties: true);

        // Assert
        Assert.False(isValid);
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(AcetoneOptions.MaxConcurrentRequests)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(1000)]
    public void AcetoneOptions_MaxConcurrentRequests_ValidRange_PassesValidation(int value)
    {
        // Arrange
        var options = new AcetoneOptions { MaxConcurrentRequests = value };
        var context = new ValidationContext(options);
        var results = new List<ValidationResult>();

        // Act
        var isValid = Validator.TryValidateObject(options, context, results, validateAllProperties: true);

        // Assert
        Assert.True(isValid);
    }
}
