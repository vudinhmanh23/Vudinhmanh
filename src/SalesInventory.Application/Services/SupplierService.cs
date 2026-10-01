using SalesInventory.Domain.Entities;
using SalesInventory.Application.Interfaces;

namespace SalesInventory.Application.Services;

public class SupplierService : ISupplierService
{
    private readonly IRepository<Supplier> _supplierRepository;

    public SupplierService(IRepository<Supplier> supplierRepository)
    {
        _supplierRepository = supplierRepository;
    }

    public async Task<IEnumerable<Supplier>> GetSuppliersAsync()
    {
        return await _supplierRepository.GetAllAsync();
    }

    public async Task<Supplier?> GetSupplierAsync(int id)
    {
        return await _supplierRepository.GetByIdAsync(id);
    }

    public async Task<Supplier> CreateSupplierAsync(Supplier supplier)
    {
        // Business rule: a supplier must have a non-empty name
        if (string.IsNullOrWhiteSpace(supplier.Name))
        {
            throw new ArgumentException("Supplier name cannot be empty.", nameof(supplier));
        }

        await _supplierRepository.AddAsync(supplier);
        await _supplierRepository.SaveChangesAsync();

        return supplier;
    }

    public async Task<bool> UpdateSupplierAsync(int id, Supplier supplier)
    {
        var existing = await _supplierRepository.GetByIdAsync(id);
        if (existing is null)
        {
            return false;
        }

        // Business rule: a supplier must have a non-empty name
        if (string.IsNullOrWhiteSpace(supplier.Name))
        {
            throw new ArgumentException("Supplier name cannot be empty.", nameof(supplier));
        }

        existing.Name = supplier.Name;
        existing.Phone = supplier.Phone;
        existing.Email = supplier.Email;
        existing.Address = supplier.Address;

        _supplierRepository.Update(existing);
        await _supplierRepository.SaveChangesAsync();

        return true;
    }

    public async Task<bool> DeleteSupplierAsync(int id)
    {
        var existing = await _supplierRepository.GetByIdAsync(id);
        if (existing is null)
        {
            return false;
        }

        _supplierRepository.Delete(existing);
        await _supplierRepository.SaveChangesAsync();

        return true;
    }
}
