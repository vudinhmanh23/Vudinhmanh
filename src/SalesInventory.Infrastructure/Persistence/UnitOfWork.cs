using SalesInventory.Application.Interfaces;

namespace SalesInventory.Infrastructure.Persistence;

// Wraps an EF Core database transaction on the shared scoped AppDbContext
public class UnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _context;

    public UnitOfWork(AppDbContext context)
    {
        _context = context;
    }

    public async Task<IUnitOfWorkTransaction> BeginTransactionAsync()
    {
        var transaction = await _context.Database.BeginTransactionAsync();
        return new EfTransaction(transaction);
    }

    private sealed class EfTransaction : IUnitOfWorkTransaction
    {
        private readonly Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction _transaction;

        public EfTransaction(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction)
        {
            _transaction = transaction;
        }

        public Task CommitAsync() => _transaction.CommitAsync();

        public Task RollbackAsync() => _transaction.RollbackAsync();

        public ValueTask DisposeAsync() => _transaction.DisposeAsync();
    }
}
