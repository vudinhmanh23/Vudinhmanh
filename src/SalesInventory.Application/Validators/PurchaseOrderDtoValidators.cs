using FluentValidation;
using SalesInventory.Application.Dtos;

namespace SalesInventory.Application.Validators;

// Shape rules only; "supplier/product exists" needs the database and lives in PurchaseOrderService
public class CreatePurchaseOrderDtoValidator : AbstractValidator<CreatePurchaseOrderDto>
{
    public CreatePurchaseOrderDtoValidator()
    {
        RuleFor(x => x.SupplierId).GreaterThan(0);
        RuleFor(x => x.OrderDate).NotEmpty();
        RuleFor(x => x.Note).MaximumLength(500);

        RuleFor(x => x.Items).NotEmpty().WithMessage("A purchase order must contain at least one item.");
        RuleForEach(x => x.Items).SetValidator(new CreatePurchaseOrderItemDtoValidator());
    }
}

public class CreatePurchaseOrderItemDtoValidator : AbstractValidator<CreatePurchaseOrderItemDto>
{
    public CreatePurchaseOrderItemDtoValidator()
    {
        RuleFor(x => x.ProductId).GreaterThan(0);
        RuleFor(x => x.Quantity).GreaterThan(0);
        RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0);
    }
}
