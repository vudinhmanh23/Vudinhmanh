namespace SalesInventory.Application.Exceptions;

// A referenced record (supplier, product, ...) does not exist; the API maps this to 404 ProblemDetails
public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message)
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
