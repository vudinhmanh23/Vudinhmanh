using AutoMapper;
using SalesInventory.Application.Dtos;
using SalesInventory.Domain.Entities;

namespace SalesInventory.Application.Mapping;

// AutoMapper rules between Customer and its DTOs
public class CustomerProfile : Profile
{
    public CustomerProfile()
    {
        CreateMap<Customer, CustomerDto>();
        // Id/CreatedAt are server-controlled
        CreateMap<CreateCustomerDto, Customer>()
            .ForMember(c => c.Id, o => o.Ignore())
            .ForMember(c => c.CreatedAt, o => o.Ignore())
            .ForMember(c => c.SalesOrders, o => o.Ignore());
        CreateMap<UpdateCustomerDto, Customer>()
            .ForMember(c => c.Id, o => o.Ignore())
            .ForMember(c => c.CreatedAt, o => o.Ignore())
            .ForMember(c => c.SalesOrders, o => o.Ignore());
    }
}
