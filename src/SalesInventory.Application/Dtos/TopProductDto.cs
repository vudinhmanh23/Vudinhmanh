namespace SalesInventory.Application.Dtos;

// One row of the best-sellers list: units sold and revenue of a product over the report window
public record TopProductDto(int ProductId, string Name, int QuantitySold, decimal Revenue);
