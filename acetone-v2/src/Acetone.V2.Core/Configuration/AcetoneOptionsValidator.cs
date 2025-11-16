using Microsoft.Extensions.Options;
using System.ComponentModel.DataAnnotations;

namespace Acetone.V2.Core.Configuration;

/// <summary>
/// Validates <see cref="AcetoneOptions"/> configuration.
/// </summary>
public class AcetoneOptionsValidator : IValidateOptions<AcetoneOptions>
{
    /// <summary>
    /// Validates the specified options instance.
    /// </summary>
    /// <param name="name">The name of the options instance.</param>
    /// <param name="options">The options instance to validate.</param>
    /// <returns>Validation result.</returns>
    public ValidateOptionsResult Validate(string? name, AcetoneOptions options)
    {
        if (options == null)
        {
            return ValidateOptionsResult.Fail("AcetoneOptions cannot be null");
        }

        var failures = new List<string>();

        // Validate using DataAnnotations
        var context = new ValidationContext(options);
        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(options, context, results, validateAllProperties: true))
        {
            failures.AddRange(results.Select(r => $"{string.Join(", ", r.MemberNames)}: {r.ErrorMessage}"));
        }

        // Custom validation
        if (options.Resilience == null)
        {
            failures.Add("Resilience configuration is required");
        }

        if (failures.Count > 0)
        {
            return ValidateOptionsResult.Fail(string.Join("; ", failures));
        }

        return ValidateOptionsResult.Success;
    }
}
