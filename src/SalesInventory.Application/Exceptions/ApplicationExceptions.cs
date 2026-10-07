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

public record StockShortage(int ProductId, string ProductName, int Requested, int Available)
{
    // How many more units than the shelf holds this order asked for
    public int Missing => Requested - Available;
}

// One or more order lines need more stock than is on hand; the API maps this to 409 ProblemDetails
public class InsufficientStockException : Exception
{
    public IReadOnlyList<StockShortage> Shortages { get; }

    public InsufficientStockException(IReadOnlyList<StockShortage> shortages)
        : base(string.Join(" ", shortages.Select(s =>
            $"Không đủ tồn kho cho sản phẩm '{s.ProductName}' (Id {s.ProductId}): cần {s.Requested}, còn {s.Available}, thiếu {s.Missing}.")))
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

// The question sent to the assistant is empty or longer than allowed; the API maps this to 400 ProblemDetails
public class ChatInputException : Exception
{
    public ChatInputException(string message) : base(message)
    {
    }
}

// The AI provider is not configured (no API key) or failed/refused the call; the API maps this to 503 ProblemDetails.
// The message is safe to show to users: provider details only go to the server log
public class AssistantUnavailableException : Exception
{
    public AssistantUnavailableException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}
