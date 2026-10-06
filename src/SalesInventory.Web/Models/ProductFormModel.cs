using System.ComponentModel.DataAnnotations;

namespace SalesInventory.Web.Models;

/// <summary>Editable fields of a product, shared by the add and edit forms. Rules match the API validators.</summary>
public class ProductFormModel
{
    // Also drives the character counter under the description box
    public const int DescriptionMaxLength = 1000;

    [Required(ErrorMessage = "Vui lòng nhập tên sản phẩm")]
    [StringLength(200, ErrorMessage = "Tên sản phẩm tối đa 200 ký tự")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập mã SKU")]
    [StringLength(32, ErrorMessage = "Mã SKU tối đa 32 ký tự")]
    [SkuFormat]
    public string Sku { get; set; } = string.Empty;

    // 0 means "nothing chosen yet" (ids start at 1)
    [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn danh mục")]
    public int CategoryId { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "Giá bán không được âm")]
    public decimal Price { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "Số lượng tồn kho không được âm")]
    public int StockQuantity { get; set; }

    [StringLength(DescriptionMaxLength, ErrorMessage = "Mô tả tối đa 1000 ký tự")]
    public string? Description { get; set; }
}


