using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SalesInventory.Application.Interfaces;
using SalesInventory.Application.Services;
using SalesInventory.Domain.Entities;

namespace SalesInventory.Tests.Support;

// Everything the stock-related services need, kept in memory: products with their stock, suppliers, customers, the orders that
// were saved and the stock-ledger rows that were written. The repositories are Moq mocks whose answers come from these
// collections, so a test can look at the real outcome (how much stock is left, which rows were written) instead of only
// checking which methods were called.
//
// Unlike a real database it does not undo changes on rollback. That is fine here: the services check the stock before they
// change anything, so a refused operation leaves the products exactly as they were, and the tests assert exactly that.
internal sealed class InventoryHarness
{
    public Dictionary<int, Product> Products { get; } = new();

    public Dictionary<int, Supplier> Suppliers { get; } = new();

    public Dictionary<int, Customer> Customers { get; } = new();

    public List<PurchaseOrder> PurchaseOrders { get; } = new();

    public List<SalesOrder> SalesOrders { get; } = new();

    public List<StockMovement> Movements { get; } = new();

    public RecordingUnitOfWork UnitOfWork { get; } = new();

    public PurchaseOrderService PurchaseOrderService { get; }

    public SalesOrderService SalesOrderService { get; }

    public StockMovementService StockMovementService { get; }

    public InventoryHarness()
    {
        var productRepository = new Mock<IProductRepository>();
        productRepository.Setup(r => r.GetByIdAsync(It.IsAny<int>()))
            .ReturnsAsync((int id) => Products.GetValueOrDefault(id));
        productRepository.Setup(r => r.GetByIdsAsync(It.IsAny<IReadOnlyCollection<int>>()))
            .ReturnsAsync((IReadOnlyCollection<int> ids) => (IReadOnlyList<Product>)ids.Where(Products.ContainsKey).Select(id => Products[id]).ToList());
        productRepository.Setup(r => r.GetExistingIdsAsync(It.IsAny<IReadOnlyCollection<int>>()))
            .ReturnsAsync((IReadOnlyCollection<int> ids) => (IReadOnlyList<int>)ids.Where(Products.ContainsKey).ToList());

        var movementRepository = new Mock<IStockMovementRepository>();
        movementRepository.Setup(r => r.AddAsync(It.IsAny<StockMovement>()))
            .Callback<StockMovement>(Movements.Add)
            .Returns(Task.CompletedTask);
        movementRepository.Setup(r => r.GetByProductAsync(It.IsAny<int>()))
            .ReturnsAsync((int id) => (IReadOnlyList<StockMovement>)Movements.Where(m => m.ProductId == id).ToList());

        var supplierRepository = new Mock<IRepository<Supplier>>();
        supplierRepository.Setup(r => r.GetByIdAsync(It.IsAny<int>())).ReturnsAsync((int id) => Suppliers.GetValueOrDefault(id));

        var customerRepository = new Mock<IRepository<Customer>>();
        customerRepository.Setup(r => r.GetByIdAsync(It.IsAny<int>())).ReturnsAsync((int id) => Customers.GetValueOrDefault(id));

        var purchaseOrderRepository = new Mock<IPurchaseOrderRepository>();
        purchaseOrderRepository.Setup(r => r.AddAsync(It.IsAny<PurchaseOrder>()))
            .Callback<PurchaseOrder>(order =>
            {
                order.Id = PurchaseOrders.Count + 1;
                PurchaseOrders.Add(order);
            })
            .Returns(Task.CompletedTask);
        // The real repository includes the lines and their products; here the same links are made from the product list
        purchaseOrderRepository.Setup(r => r.GetWithItemsAsync(It.IsAny<int>()))
            .ReturnsAsync((int id) =>
            {
                var order = PurchaseOrders.FirstOrDefault(o => o.Id == id);
                foreach (var item in order?.PurchaseOrderItems ?? Enumerable.Empty<PurchaseOrderItem>())
                {
                    item.Product = Products.GetValueOrDefault(item.ProductId);
                }

                return order;
            });

        // The next code depends on the last one already saved with the same prefix, as in the real repository
        purchaseOrderRepository.Setup(r => r.GetLastCodeWithPrefixAsync(It.IsAny<string>()))
            .ReturnsAsync((string prefix) => PurchaseOrders.Select(o => o.Code).Where(c => c.StartsWith(prefix)).OrderBy(c => c).LastOrDefault());

        var salesOrderRepository = new Mock<ISalesOrderRepository>();
        salesOrderRepository.Setup(r => r.AddAsync(It.IsAny<SalesOrder>()))
            .Callback<SalesOrder>(order =>
            {
                order.Id = SalesOrders.Count + 1;
                SalesOrders.Add(order);
            })
            .Returns(Task.CompletedTask);

        PurchaseOrderService = new PurchaseOrderService(
            purchaseOrderRepository.Object, supplierRepository.Object, productRepository.Object,
            movementRepository.Object, UnitOfWork, NullLogger<PurchaseOrderService>.Instance);

        SalesOrderService = new SalesOrderService(
            salesOrderRepository.Object, customerRepository.Object, productRepository.Object,
            movementRepository.Object, UnitOfWork, NullLogger<SalesOrderService>.Instance);

        StockMovementService = new StockMovementService(movementRepository.Object, productRepository.Object, UnitOfWork);
    }

    public Product AddProduct(int id, int stock, string? name = null, int lowStockThreshold = 0)
    {
        var product = new Product
        {
            Id = id,
            Name = name ?? $"Product {id}",
            Sku = $"SKU-{id:000}",
            StockQuantity = stock,
            LowStockThreshold = lowStockThreshold
        };
        Products[id] = product;
        return product;
    }

    public Supplier AddSupplier(int id = 1)
    {
        var supplier = new Supplier { Id = id, Code = $"SUP-{id:000}", Name = $"Supplier {id}" };
        Suppliers[id] = supplier;
        return supplier;
    }

    public Customer AddCustomer(int id = 1)
    {
        var customer = new Customer { Id = id, Name = $"Customer {id}" };
        Customers[id] = customer;
        return customer;
    }

    // A purchase order with one line per (productId, quantity) pair
    public static PurchaseOrder PurchaseOrderOf(int supplierId, params (int ProductId, int Quantity)[] lines) => new()
    {
        SupplierId = supplierId,
        PurchaseOrderItems = lines.Select(l => new PurchaseOrderItem { ProductId = l.ProductId, Quantity = l.Quantity, UnitPrice = 1000 }).ToList()
    };

    // A sales order with one line per (productId, quantity) pair
    public static SalesOrder SalesOrderOf(int customerId, params (int ProductId, int Quantity)[] lines) => new()
    {
        CustomerId = customerId,
        Items = lines.Select(l => new SalesOrderItem { ProductId = l.ProductId, Quantity = l.Quantity, UnitPrice = 25000 }).ToList()
    };
}
