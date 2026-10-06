namespace SalesInventory.Application.Exceptions;

// A referenced record (supplier, product, ...) does not exist; the API maps this to 404 ProblemDetails
public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message)
    {
    }
}

// The request conflicts with the current state of the record; the API maps this to 409 ProblemDetails
public class ConflictException : Exception
{
    public ConflictException(string message) : base(message)
    {
    }
}

// The request is well-formed but semantically unacceptable (e.g. a duplicate SKU in an import); the API maps this to 422 ProblemDetails
public class UnprocessableEntityException : Exception
{
    public UnprocessableEntityException(string message) : base(message)
    {
    }
}

public record StockShortage(int ProductId, string ProductName, int Requested, int Available);

// One or more order lines need more stock than is on hand; the API maps this to 409 ProblemDetails
public class InsufficientStockException : Exception
{
    public IReadOnlyList<StockShortage> Shortages { get; }

    public InsufficientStockException(IReadOnlyList<StockShortage> shortages)
        : base(string.Join(" ", shortages.Select(s =>
            $"Không đủ tồn kho cho sản phẩm '{s.ProductName}' (Id {s.ProductId}): cần {s.Requested}, còn {s.Available}.")))
    {
        Shortages = shortages;
    }
}

// A concurrent request won a race on the same rows (stale RowVersion, duplicate order number, deadlock victim).
// Safe to retry from scratch; the API maps this to 409 ProblemDetails once retries are exhausted.
public class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}

// A business rule was violated; the API maps this to 400 ProblemDetails
public class BusinessRuleException : Exception
{
    public BusinessRuleException(string message) : base(message)
    {
    }
}
