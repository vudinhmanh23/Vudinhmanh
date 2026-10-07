namespace SalesInventory.Application.Interfaces;

// Lets services group several repository saves into one all-or-nothing database transaction
public interface IUnitOfWork
{
    Task<IUnitOfWorkTransaction> BeginTransactionAsync();

    // Forgets every entity loaded or changed so far, so a retry re-reads fresh rows instead of stale tracked copies
    void ResetTracking();
}

// Disposing without calling CommitAsync rolls the transaction back
public interface IUnitOfWorkTransaction : IAsyncDisposable
{
    Task CommitAsync();
    Task RollbackAsync();
}
