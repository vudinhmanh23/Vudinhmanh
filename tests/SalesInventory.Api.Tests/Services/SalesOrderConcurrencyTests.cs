using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SalesInventory.Application.Exceptions;
using SalesInventory.Application.Services;
using SalesInventory.Domain.Entities;
using SalesInventory.Domain.Enums;
using SalesInventory.Infrastructure.Persistence;
using SalesInventory.Infrastructure.Repositories;

namespace SalesInventory.Api.Tests.Services;

// Skipped (not failed) when SQL Server LocalDB is not installed on the machine running the tests
public sealed class LocalDbFactAttribute : FactAttribute
{
    public LocalDbFactAttribute()
    {
        try
        {
            using var connection = new SqlConnection(SalesOrderConcurrencyTests.MasterConnectionString);
            connection.Open();
        }
        catch (Exception ex)
        {
            Skip = $"SQL Server LocalDB is not available: {ex.GetType().Name}";
        }
    }
}

// Real SQL Server engine (throwaway LocalDB database): real rowversion, real row locks, real transactions.
// The EF InMemory/SQLite providers cannot prove any of this.
public sealed class SalesOrderConcurrencyTests : IDisposable
{
    public const string MasterConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Database=master;Integrated Security=true;Connect Timeout=5;TrustServerCertificate=true";

    private readonly string _databaseName = $"SalesInventory_RaceTests_{Guid.NewGuid():N}";
    private string ConnectionString =>
        $@"Server=(localdb)\MSSQLLocalDB;Database={_databaseName};Integrated Security=true;TrustServerCertificate=true";

    private AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options);

    // Every buyer gets its own context, repositories and service, exactly like separate HTTP requests
    private SalesOrderService NewService(AppDbContext db) => new(
        new SalesOrderRepository(db),
        new Repository<Customer>(db),
        new Repository<Product>(db),
        new Repository<StockMovement>(db),
        new UnitOfWork(db),
        NullLogger<SalesOrderService>.Instance);

    [LocalDbFact]
    public async Task ParallelSales_OfTheSameProduct_NeverMakeStockNegative_AndLedgerMatches()
    {
        const int initialStock = 10;
        const int perOrder = 3;      // at most 3 of the buyers can fit: 3 * 3 = 9 <= 10 < 12
        const int buyers = 8;
        const int rounds = 5;

        int customerId;
        await using (var setup = NewContext())
        {
            await setup.Database.EnsureCreatedAsync();
            var customer = new Customer { Name = "Race customer" };
            setup.Customers.Add(customer);
            await setup.SaveChangesAsync();
            customerId = customer.Id;
        }

        for (var round = 0; round < rounds; round++)
        {
            await using (var reset = NewContext())
            {
                await reset.Products.Where(p => p.Id == 1).ExecuteUpdateAsync(s => s.SetProperty(p => p.StockQuantity, initialStock));
                await reset.StockMovements.ExecuteDeleteAsync();
                await reset.SalesOrderItems.ExecuteDeleteAsync();
                await reset.SalesOrders.ExecuteDeleteAsync();
            }

            var start = new TaskCompletionSource();
            var tasks = Enumerable.Range(0, buyers).Select(async _ =>
            {
                await using var db = NewContext();
                var service = NewService(db);
                var order = new SalesOrder
                {
                    CustomerId = customerId,
                    OrderDate = DateTime.UtcNow,
                    Items = { new SalesOrderItem { ProductId = 1, Quantity = perOrder, UnitPrice = 100m } }
                };

                await start.Task; // release all buyers at the same moment
                try
                {
                    await service.CreateOrderAsync(order);
                    return (Exception?)null;
                }
                catch (Exception ex)
                {
                    return ex;
                }
            }).ToList();

            start.SetResult();
            var results = await Task.WhenAll(tasks);

            var successes = results.Count(r => r is null);
            // The only acceptable failures are the 409 kinds; anything else (500, duplicate key...) is a bug
            Assert.All(results.Where(r => r is not null),
                r => Assert.True(r is InsufficientStockException or ConcurrencyConflictException, $"Unexpected failure: {r}"));

            await using var verify = NewContext();
            var stock = (await verify.Products.AsNoTracking().SingleAsync(p => p.Id == 1)).StockQuantity;

            Assert.True(stock >= 0, $"Stock went negative: {stock}");
            Assert.InRange(successes, 1, initialStock / perOrder);
            Assert.Equal(initialStock - successes * perOrder, stock); // every success deducted exactly once, no lost update
            Assert.Equal(successes, await verify.SalesOrders.CountAsync());
            Assert.Equal(successes, await verify.StockMovements.CountAsync(m => m.MovementType == StockMovementType.Sale));
            Assert.Equal(successes, await verify.SalesOrders.Select(o => o.OrderNumber).Distinct().CountAsync());

            // Ledger is consistent: the smallest StockAfter is exactly the final stock
            Assert.Equal(stock, await verify.StockMovements.MinAsync(m => m.StockAfter));
        }
    }

    // The stock-row guard on its own: in the full service race above, the unique order number also makes
    // buyers collide early, so this test pins the RowVersion behaviour that protects the deduction itself
    [LocalDbFact]
    public async Task TwoWritersOnTheSameProductRow_SecondOneIsRejected_SoNoDeductionIsLost()
    {
        await using (var setup = NewContext())
        {
            await setup.Database.EnsureCreatedAsync();
            await setup.Products.Where(p => p.Id == 1).ExecuteUpdateAsync(s => s.SetProperty(p => p.StockQuantity, 10));
        }

        await using var first = NewContext();
        await using var second = NewContext();
        var a = await first.Products.SingleAsync(p => p.Id == 1);
        var b = await second.Products.SingleAsync(p => p.Id == 1); // both saw stock 10

        a.StockQuantity -= 7;
        await first.SaveChangesAsync();

        b.StockQuantity -= 7; // would be 3 again if the stale write were accepted (lost update)
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());

        await using var verify = NewContext();
        Assert.Equal(3, (await verify.Products.AsNoTracking().SingleAsync(p => p.Id == 1)).StockQuantity);
    }

    // The InMemory provider would accept any LINQ; running on SQL Server proves the filter and ordering translate to SQL
    [LocalDbFact]
    public async Task LowStockQuery_RunsAsSqlOnSqlServer_WithFilterAndShortageOrdering()
    {
        await using (var setup = NewContext())
        {
            await setup.Database.EnsureCreatedAsync();
            // Seeded stock: product 1 = 50, 2 = 100, 3 = 500, 4 = 200
            await setup.Products.Where(p => p.Id == 1).ExecuteUpdateAsync(s => s.SetProperty(p => p.ReorderLevel, 60));   // shortage 10
            await setup.Products.Where(p => p.Id == 2).ExecuteUpdateAsync(s => s.SetProperty(p => p.ReorderLevel, 120));  // shortage 20
            await setup.Products.Where(p => p.Id == 3).ExecuteUpdateAsync(s => s.SetProperty(p => p.ReorderLevel, 0));    // not tracked
            await setup.Products.Where(p => p.Id == 4).ExecuteUpdateAsync(s => s.SetProperty(p => p.ReorderLevel, 300).SetProperty(p => p.IsActive, false));
        }

        await using var db = NewContext();
        var result = await new ProductRepository(db).GetBelowReorderLevelAsync();

        Assert.Equal(new[] { 2, 1 }, result.Select(p => p.Id));
    }

    // Cross-checks the repository's aggregates against a hand-written SQL statement and against decimal math in C#
    [LocalDbFact]
    public async Task InventorySummary_MatchesAReferenceSqlQuery_ToTheLastCent()
    {
        await using (var setup = NewContext())
        {
            await setup.Database.EnsureCreatedAsync();
            // Awkward cents on purpose: binary floating point would drift here, decimal must not
            await setup.Products.Where(p => p.Id == 1).ExecuteUpdateAsync(s => s
                .SetProperty(p => p.PurchasePrice, 12345.67m).SetProperty(p => p.ReorderLevel, 60));            // stock 50, low
            await setup.Products.Where(p => p.Id == 2).ExecuteUpdateAsync(s => s.SetProperty(p => p.PurchasePrice, 0.01m)); // stock 100
            await setup.Products.Where(p => p.Id == 3).ExecuteUpdateAsync(s => s
                .SetProperty(p => p.PurchasePrice, 0.07m).SetProperty(p => p.IsActive, false));                 // stock 500, inactive
        }

        await using var db = NewContext();
        var summary = await new ProductRepository(db).GetInventorySummaryAsync();

        // Reference: plain SQL written independently of the LINQ in the repository
        var sqlValue = await db.Database.SqlQueryRaw<decimal>("SELECT ISNULL(SUM(StockQuantity * PurchasePrice), 0) AS Value FROM Products").SingleAsync();
        var sqlTotal = await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM Products").SingleAsync();
        var sqlActive = await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM Products WHERE IsActive = 1").SingleAsync();
        var sqlLow = await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS Value FROM Products WHERE IsActive = 1 AND ReorderLevel > 0 AND StockQuantity <= ReorderLevel").SingleAsync();

        Assert.Equal(sqlValue, summary.InventoryValue);
        Assert.Equal(sqlTotal, summary.TotalProducts);
        Assert.Equal(sqlActive, summary.ActiveProducts);
        Assert.Equal(sqlLow, summary.LowStockProducts);

        // And against exact decimal arithmetic: 50 x 12345.67 + 100 x 0.01 + 500 x 0.07
        Assert.Equal(50 * 12345.67m + 100 * 0.01m + 500 * 0.07m, summary.InventoryValue);
        Assert.Equal(617_319.50m, summary.InventoryValue);
        Assert.Equal(8, summary.TotalProducts);
        Assert.Equal(7, summary.ActiveProducts);
        Assert.Equal(1, summary.LowStockProducts);
    }

    [LocalDbFact]
    public async Task DatabaseCheckConstraint_RejectsNegativeStock_EvenWhenAppCodeIsBypassed()
    {
        await using (var setup = NewContext())
        {
            await setup.Database.EnsureCreatedAsync();
        }

        await using var db = NewContext();
        await Assert.ThrowsAnyAsync<Exception>(() =>
            db.Products.Where(p => p.Id == 1).ExecuteUpdateAsync(s => s.SetProperty(p => p.StockQuantity, -1)));
    }

    public void Dispose()
    {
        try
        {
            using var db = NewContext();
            db.Database.EnsureDeleted();
        }
        catch
        {
            // LocalDB missing or database never created: nothing to clean up
        }
    }
}
