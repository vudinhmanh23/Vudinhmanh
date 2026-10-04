using AutoMapper;
using SalesInventory.Application.Dtos;
using SalesInventory.Domain.Entities;

namespace SalesInventory.Application.Mapping;

public class StockMovementProfile : Profile
{
    public StockMovementProfile()
    {
        // The MovementType enum is mapped to its name by AutoMapper
        CreateMap<StockMovement, StockMovementDto>();
    }
}
