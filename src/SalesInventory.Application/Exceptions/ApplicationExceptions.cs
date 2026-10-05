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

// A business rule was violated; the API maps this to 400 ProblemDetails
public class BusinessRuleException : Exception
{
    public BusinessRuleException(string message) : base(message)
    {
    }
}
