using AutoMapper;
using SalesInventory.Application.Dtos;
using SalesInventory.Domain.Entities;

namespace SalesInventory.Application.Mapping;

// AutoMapper rules between Supplier and its DTOs
public class SupplierProfile : Profile
{
    public SupplierProfile()
    {
        CreateMap<Supplier, SupplierDto>();
        // Id/CreatedAt are server-controlled; IsActive stays at its default (true) on create
        CreateMap<CreateSupplierDto, Supplier>()
            .ForMember(s => s.Id, o => o.Ignore())
            .ForMember(s => s.CreatedAt, o => o.Ignore());
        CreateMap<UpdateSupplierDto, Supplier>()
            .ForMember(s => s.Id, o => o.Ignore())
            .ForMember(s => s.CreatedAt, o => o.Ignore());
    }
}
