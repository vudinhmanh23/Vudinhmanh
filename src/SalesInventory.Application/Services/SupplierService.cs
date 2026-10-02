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

    public async Task<IEnumerable<Supplier>> GetActiveSuppliersAsync()
    {
        var suppliers = await _supplierRepository.GetAllAsync();
        return suppliers.Where(s => s.IsActive);
    }

    public async Task<Supplier?> GetSupplierAsync(int id)
    {
        return await _supplierRepository.GetByIdAsync(id);
    }

    public async Task<Supplier> CreateSupplierAsync(Supplier supplier)
    {
        await ValidateAsync(supplier, excludeId: null);

        supplier.CreatedAt = DateTime.UtcNow;

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

        await ValidateAsync(supplier, excludeId: id);

        existing.Code = supplier.Code;
        existing.Name = supplier.Name;
        existing.ContactPerson = supplier.ContactPerson;
        existing.IsActive = supplier.IsActive;
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

        // Soft delete: keep the record so products and purchase orders keep their history
        existing.IsActive = false;

        _supplierRepository.Update(existing);
        await _supplierRepository.SaveChangesAsync();

        return true;
    }

    // Business rules: name and code are required, and the code must be unique across suppliers
    private async Task ValidateAsync(Supplier supplier, int? excludeId)
    {
        if (string.IsNullOrWhiteSpace(supplier.Name))
        {
            throw new ArgumentException("Supplier name cannot be empty.", nameof(supplier));
        }

        if (string.IsNullOrWhiteSpace(supplier.Code))
        {
            throw new ArgumentException("Supplier code cannot be empty.", nameof(supplier));
        }

        supplier.Code = supplier.Code.Trim();

        var all = await _supplierRepository.GetAllAsync();
        if (all.Any(s => s.Id != excludeId && string.Equals(s.Code, supplier.Code, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException($"Supplier code '{supplier.Code}' already exists.", nameof(supplier));
        }
    }
}
