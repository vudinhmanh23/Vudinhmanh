using AutoMapper;
using SalesInventory.Application.Dtos;
using SalesInventory.Domain.Entities;

namespace SalesInventory.Application.Mapping;

// AutoMapper rules between Product and its DTOs
public class ProductProfile : Profile
{
    public ProductProfile()
    {
        // Category/Supplier names are filled by the controller from their own services
        CreateMap<Product, ProductDto>()
            .ForMember(d => d.Quantity, o => o.MapFrom(p => p.StockQuantity))
            .ForMember(d => d.CategoryName, o => o.Ignore())
            .ForMember(d => d.SupplierName, o => o.Ignore());

        // Id/CreatedAt/SupplierId are server-controlled; legacy Price mirrors SalePrice
        CreateMap<CreateProductDto, Product>()
            .ForMember(p => p.StockQuantity, o => o.MapFrom(d => d.Quantity))
            .ForMember(p => p.Price, o => o.MapFrom(d => d.SalePrice))
            .ForMember(p => p.Id, o => o.Ignore())
            .ForMember(p => p.CreatedAt, o => o.Ignore())
            .ForMember(p => p.SupplierId, o => o.Ignore())
            .ForMember(p => p.Category, o => o.Ignore())
            .ForMember(p => p.Supplier, o => o.Ignore());
        CreateMap<UpdateProductDto, Product>()
            .ForMember(p => p.StockQuantity, o => o.MapFrom(d => d.Quantity))
            .ForMember(p => p.Price, o => o.MapFrom(d => d.SalePrice))
            .ForMember(p => p.Id, o => o.Ignore())
            .ForMember(p => p.CreatedAt, o => o.Ignore())
            .ForMember(p => p.SupplierId, o => o.Ignore())
            .ForMember(p => p.Category, o => o.Ignore())
            .ForMember(p => p.Supplier, o => o.Ignore());
    }
}
