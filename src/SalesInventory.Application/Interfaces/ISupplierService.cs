using SalesInventory.Domain.Entities;

namespace SalesInventory.Application.Interfaces;

// Business-facing operations for Supplier, on top of the repository layer
public interface ISupplierService
{
    Task<IEnumerable<Supplier>> GetSuppliersAsync();
    Task<IEnumerable<Supplier>> GetActiveSuppliersAsync();
    Task<Supplier?> GetSupplierAsync(int id);
    Task<Supplier> CreateSupplierAsync(Supplier supplier);
    Task<bool> UpdateSupplierAsync(int id, Supplier supplier);
    Task<bool> DeleteSupplierAsync(int id);
}
