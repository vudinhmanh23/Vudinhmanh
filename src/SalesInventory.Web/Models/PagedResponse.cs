namespace SalesInventory.Web.Models;

/// <summary>One page of a list endpoint, as returned by the API ({ items, totalCount, page, pageSize }).</summary>
public record PagedResponse<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);
