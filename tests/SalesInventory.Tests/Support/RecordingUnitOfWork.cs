using SalesInventory.Application.Interfaces;

namespace SalesInventory.Tests.Support;

// A unit of work that does nothing but count what the service asked of it, so a test can check that a failed operation was
// rolled back and a successful one committed.
internal sealed class RecordingUnitOfWork : IUnitOfWork
{
    public int Begun { get; private set; }

    public int Committed { get; private set; }

    public int RolledBack { get; private set; }

    public Task<IUnitOfWorkTransaction> BeginTransactionAsync()
    {
        Begun++;
        return Task.FromResult<IUnitOfWorkTransaction>(new Transaction(this));
    }

    public void ResetTracking()
    {
    }

    private sealed class Transaction : IUnitOfWorkTransaction
    {
        private readonly RecordingUnitOfWork _owner;

        public Transaction(RecordingUnitOfWork owner) => _owner = owner;

        public Task CommitAsync()
        {
            _owner.Committed++;
            return Task.CompletedTask;
        }

        public Task RollbackAsync()
        {
            _owner.RolledBack++;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
