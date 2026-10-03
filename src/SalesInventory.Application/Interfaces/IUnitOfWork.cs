namespace SalesInventory.Application.Interfaces;

// Lets services group several repository saves into one all-or-nothing database transaction
public interface IUnitOfWork
{
    Task<IUnitOfWorkTransaction> BeginTransactionAsync();
}

// Disposing without calling CommitAsync rolls the transaction back
public interface IUnitOfWorkTransaction : IAsyncDisposable
{
    Task CommitAsync();
    Task RollbackAsync();
}
