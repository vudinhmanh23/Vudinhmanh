using FluentValidation;
using SalesInventory.Application.Dtos;

namespace SalesInventory.Application.Validators;

// Shared rules for the write models of a supplier
internal static class SupplierRules
{
    public static void Apply<T>(AbstractValidator<T> validator, Func<T, string> code, Func<T, string> name, Func<T, string?> email)
    {
        validator.RuleFor(x => code(x)).NotEmpty().MaximumLength(20).OverridePropertyName("Code");
        validator.RuleFor(x => name(x)).NotEmpty().MaximumLength(200).OverridePropertyName("Name");

        // Email is optional; validate the format only when a value is supplied
        validator.RuleFor(x => email(x)).EmailAddress().When(x => !string.IsNullOrWhiteSpace(email(x))).OverridePropertyName("Email");
    }
}

public class CreateSupplierDtoValidator : AbstractValidator<CreateSupplierDto>
{
    public CreateSupplierDtoValidator()
    {
        SupplierRules.Apply(this, x => x.Code, x => x.Name, x => x.Email);
    }
}

public class UpdateSupplierDtoValidator : AbstractValidator<UpdateSupplierDto>
{
    public UpdateSupplierDtoValidator()
    {
        SupplierRules.Apply(this, x => x.Code, x => x.Name, x => x.Email);
    }
}
