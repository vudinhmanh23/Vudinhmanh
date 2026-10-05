using AutoMapper;
using SalesInventory.Application.Dtos;
using SalesInventory.Domain.Entities;

namespace SalesInventory.Application.Mapping;

// AutoMapper rules between SalesOrder and its DTOs
public class SalesOrderProfile : Profile
{
    public SalesOrderProfile()
    {
        CreateMap<SalesOrder, OrderDto>()
            .ForMember(d => d.Status, o => o.MapFrom(s => s.Status.ToString()))
            .ForMember(d => d.Warnings, o => o.Ignore());
        CreateMap<SalesOrderItem, OrderItemDto>()
            .ForMember(d => d.ProductName, o => o.MapFrom(s => s.Product != null ? s.Product.Name : null));

        // Id and all money totals are server-controlled
        CreateMap<CreateOrderDto, SalesOrder>()
            .ForMember(o => o.Id, o => o.Ignore())
            .ForMember(o => o.Customer, o => o.Ignore())
            .ForMember(o => o.OrderNumber, o => o.Ignore())
            .ForMember(o => o.Status, o => o.Ignore())
            .ForMember(o => o.TotalAmount, o => o.Ignore());
        CreateMap<CreateOrderItemDto, SalesOrderItem>()
            .ForMember(i => i.Id, o => o.Ignore())
            .ForMember(i => i.SalesOrderId, o => o.Ignore())
            .ForMember(i => i.SalesOrder, o => o.Ignore())
            .ForMember(i => i.Product, o => o.Ignore())
            .ForMember(i => i.LineTotal, o => o.Ignore());
    }
}
