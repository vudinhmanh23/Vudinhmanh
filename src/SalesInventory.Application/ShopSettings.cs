namespace SalesInventory.Application;

// Bound from the "Shop" configuration section; printed in the header of the PDF documents
public class ShopSettings
{
    public string Name { get; set; } = "Cửa hàng SalesInventory";

    public string? Address { get; set; }

    public string? Phone { get; set; }

    // Optional: school name and student id shown under the shop name (for the course project)
    public string? SchoolName { get; set; }

    public string? StudentId { get; set; }

    // Optional path of a PNG/JPG logo shown at the top left; relative paths resolve against the working directory
    public string? LogoPath { get; set; }
}
