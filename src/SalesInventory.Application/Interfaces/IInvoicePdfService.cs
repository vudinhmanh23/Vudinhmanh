using SalesInventory.Domain.Entities;

namespace SalesInventory.Application.Interfaces;

public interface IInvoicePdfService
{
    // Renders the invoice of an order (with Customer and Items.Product loaded) and returns the PDF bytes
    byte[] Generate(SalesOrder order);
}
