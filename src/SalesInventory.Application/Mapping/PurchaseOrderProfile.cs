using AutoMapper;
using SalesInventory.Application.Dtos;
using SalesInventory.Domain.Entities;

namespace SalesInventory.Application.Mapping;

// AutoMapper rules between PurchaseOrder (and its items) and their DTOs
public class PurchaseOrderProfile : Profile
{
    public PurchaseOrderProfile()
    {
        CreateMap<PurchaseOrderItem, PurchaseOrderItemDto>()
            .ForMember(d => d.ProductName, o => o.MapFrom(s => s.Product != null ? s.Product.Name : null));

        CreateMap<PurchaseOrder, PurchaseOrderDto>()
            .ForMember(d => d.Items, o => o.MapFrom(s => s.PurchaseOrderItems));

        // Id and all money fields are server-controlled: the service computes them
        CreateMap<CreatePurchaseOrderItemDto, PurchaseOrderItem>()
            .ForMember(i => i.Id, o => o.Ignore())
            .ForMember(i => i.PurchaseOrderId, o => o.Ignore())
            .ForMember(i => i.PurchaseOrder, o => o.Ignore())
            .ForMember(i => i.Product, o => o.Ignore())
            .ForMember(i => i.LineTotal, o => o.Ignore());

        CreateMap<CreatePurchaseOrderDto, PurchaseOrder>()
            .ForMember(p => p.Id, o => o.Ignore())
            .ForMember(p => p.Code, o => o.Ignore())
            .ForMember(p => p.Status, o => o.Ignore())
            .ForMember(p => p.Supplier, o => o.Ignore())
            .ForMember(p => p.TotalAmount, o => o.Ignore())
            .ForMember(p => p.PurchaseOrderItems, o => o.MapFrom(s => s.Items));
    }
}
