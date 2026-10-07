using System.Linq.Expressions;

namespace SalesInventory.Application.Interfaces;

// Generic data-access contract shared by all entities
public interface IRepository<T> where T : class
{
    // Read-only: the returned entities are not tracked, so do not modify and save them (use GetByIdAsync for that)
    Task<IEnumerable<T>> GetAllAsync();
    Task<T?> GetByIdAsync(int id);

    // Existence check and row count done by the database, without loading entities
    Task<bool> AnyAsync(Expression<Func<T, bool>> predicate);
    Task<int> CountAsync();
    Task AddAsync(T entity);
    void Update(T entity);
    void Delete(T entity);
    Task SaveChangesAsync();
}
