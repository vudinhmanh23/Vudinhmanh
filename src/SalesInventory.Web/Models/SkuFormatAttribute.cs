using System.ComponentModel.DataAnnotations;

namespace SalesInventory.Web.Models;

/// <summary>
/// Checks the shape of a SKU with a specific Vietnamese message per mistake.
/// Same rules as the API (upper-case letters, digits and '-', 3-32 characters); '-' is allowed because
/// existing SKUs such as "SKU-DT-001" use it as a separator. An empty value is left to [Required].
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class SkuFormatAttribute : ValidationAttribute
{
    private const int MinLength = 3;

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not string sku || sku.Length == 0)
        {
            return ValidationResult.Success;
        }

        // Checked first: a space is the most common slip and deserves its own message
        if (sku.Any(char.IsWhiteSpace))
        {
            return Fail(validationContext, "Mã SKU không được chứa dấu cách");
        }

        // ASCII only: this also rejects Vietnamese diacritics, which char.IsLetter would accept
        if (sku.Any(c => !(IsAsciiLetterOrDigit(c) || c == '-')))
        {
            return Fail(validationContext, "Mã SKU chỉ gồm chữ không dấu, số hoặc dấu '-'");
        }

        if (sku.Any(c => c is >= 'a' and <= 'z'))
        {
            return Fail(validationContext, "Mã SKU phải viết in hoa");
        }

        if (sku.Length < MinLength)
        {
            return Fail(validationContext, $"Mã SKU phải có ít nhất {MinLength} ký tự");
        }

        return ValidationResult.Success;
    }

    private static bool IsAsciiLetterOrDigit(char c) => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9';

    // MemberNames ties the message to the Sku field so <ValidationMessage For="..."> shows it under that input
    private static ValidationResult Fail(ValidationContext context, string message) =>
        new(message, context.MemberName is { } member ? new[] { member } : null);
}
