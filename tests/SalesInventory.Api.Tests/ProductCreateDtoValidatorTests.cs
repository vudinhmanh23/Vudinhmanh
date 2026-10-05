using Moq;
using SalesInventory.Application.Dtos;
using SalesInventory.Application.Interfaces;
using SalesInventory.Application.Validators;

namespace SalesInventory.Api.Tests;

// Unit tests for CreateProductDtoValidator (no DB: IProductService is mocked)
public class ProductCreateDtoValidatorTests
{
    private static CreateProductDtoValidator CreateValidator(bool skuTaken = false)
    {
        var service = new Mock<IProductService>();
        service.Setup(s => s.IsSkuTakenAsync(It.IsAny<string>(), null)).ReturnsAsync(skuTaken);
        return new CreateProductDtoValidator(service.Object);
    }

    private static CreateProductDto ValidDto() => new()
    {
        Name = "Bàn phím cơ",
        Sku = "KB-001",
        PurchasePrice = 300000,
        SalePrice = 450000,
        CategoryId = 1
    };

    [Fact]
    public async Task ValidDto_IsValid()
    {
        var result = await CreateValidator().ValidateAsync(ValidDto());

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task SalePriceBelowPurchasePrice_FailsOnSalePrice()
    {
        var dto = ValidDto();
        dto.SalePrice = 200000;

        var result = await CreateValidator().ValidateAsync(dto);

        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors);
        Assert.Equal("SalePrice", error.PropertyName);
        Assert.Equal("Giá bán không được nhỏ hơn giá nhập", error.ErrorMessage);
    }

    [Fact]
    public async Task InvalidSkuFormat_FailsOnSku()
    {
        var dto = ValidDto();
        dto.Sku = "kb";

        var result = await CreateValidator().ValidateAsync(dto);

        Assert.Contains(result.Errors, e => e.PropertyName == "Sku");
    }

    [Fact]
    public async Task ExistingSku_FailsOnSku()
    {
        var result = await CreateValidator(skuTaken: true).ValidateAsync(ValidDto());

        Assert.Contains(result.Errors, e => e.PropertyName == "Sku");
    }
}
