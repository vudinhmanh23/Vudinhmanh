namespace SalesInventory.Web.Models;

public record CustomerItem(int Id, string Name, string? Phone);

/// <summary>Body of POST /api/customers (quick-add from the POS screen).</summary>
public record CustomerRequest(string Name, string? Phone);

/// <summary>One line of a cart: a sale (UnitPrice = sale price) or a purchase order (UnitPrice = unit cost).</summary>
public class CartLineVm
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }

    // Stock on hand when the product list was last loaded; used to block over-selling before the API does.
    // Null means "no limit" (purchase lines add stock instead of taking it).
    public int? Stock { get; set; }

    public decimal LineTotal => Quantity * UnitPrice;

    // Why this line cannot be submitted, or null when it is fine
    public string? Problem
    {
        get
        {
            if (Quantity < 1)
            {
                return "Số lượng phải lớn hơn 0";
            }

            return Stock is int stock && Quantity > stock ? $"Chỉ còn {stock} {Unit} trong kho" : null;
        }
    }
}

/// <summary>Payload of CartTable.OnQuantityChanged: which line, and the quantity the user wants.</summary>
public record QuantityChange(CartLineVm Line, int Quantity);

/// <summary>Body of POST /api/sales-orders.</summary>
public record SalesOrderRequest(
    DateTime OrderDate,
    int CustomerId,
    decimal DiscountAmount,
    string? Note,
    IReadOnlyList<SalesItemRequest> Items);

public record SalesItemRequest(int ProductId, int Quantity, decimal UnitPrice);

/// <summary>The part of the API's OrderDto we need after a sale. Warnings are low-stock notices; they never block the order.</summary>
public record SalesOrderResult(int Id, string OrderNumber, List<string>? Warnings);
