namespace SalesInventory.Api.Dtos;

// Read model returned by category endpoints
public class CategoryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}
