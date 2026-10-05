using FluentValidation;
using SalesInventory.Application.Dtos;

namespace SalesInventory.Application.Validators;

// Shared rules for the write models of a supplier
internal static class SupplierRules
{
    public static void Apply<T>(AbstractValidator<T> validator, Func<T, string> code, Func<T, string> name, Func<T, string?> email, Func<T, string?> phone)
    {
        validator.RuleFor(x => code(x)).NotEmpty().MaximumLength(20).OverridePropertyName("Code");
        validator.RuleFor(x => name(x)).NotEmpty().MaximumLength(200).OverridePropertyName("Name");

        // Phone is optional; when present it must be a Vietnamese number of 10-11 digits
        validator.RuleFor(x => phone(x))
            .Matches(@"^\d{10,11}$")
            .WithMessage("Số điện thoại phải gồm 10-11 chữ số")
            .When(x => !string.IsNullOrWhiteSpace(phone(x)))
            .OverridePropertyName("Phone");

        // Email is optional; validate the format only when a value is supplied
        validator.RuleFor(x => email(x)).EmailAddress().When(x => !string.IsNullOrWhiteSpace(email(x))).OverridePropertyName("Email");
    }
}

public class CreateSupplierDtoValidator : AbstractValidator<CreateSupplierDto>
{
    public CreateSupplierDtoValidator()
    {
        SupplierRules.Apply(this, x => x.Code, x => x.Name, x => x.Email, x => x.Phone);
    }
}

public class UpdateSupplierDtoValidator : AbstractValidator<UpdateSupplierDto>
{
    public UpdateSupplierDtoValidator()
    {
        SupplierRules.Apply(this, x => x.Code, x => x.Name, x => x.Email, x => x.Phone);
    }
}
