namespace SalesInventory.Web.Models;

/// <summary>Active supplier shown in the supplier dropdown.</summary>
public record SupplierItem(int Id, string Code, string Name);

/// <summary>Body of POST /api/purchase-orders. The API names the per-unit cost "unitPrice".</summary>
public record PurchaseOrderRequest(DateTime OrderDate, int SupplierId, string? Note, IReadOnlyList<PurchaseItemRequest> Items);

public record PurchaseItemRequest(int ProductId, int Quantity, decimal UnitPrice);

/// <summary>The part of the API's PurchaseOrderDto we need after creating an order.</summary>
public record PurchaseOrderResult(int Id, string Code);
