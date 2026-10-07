using System.Net.Http.Json;
using SalesInventory.Web.Models;

namespace SalesInventory.Web.Services;

/// <summary>Read-only lookups shared by several pages. Every method returns null when the list could not be loaded.</summary>
public class CatalogApi
{
    private readonly IHttpClientFactory _httpClientFactory;

    public CatalogApi(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public Task<List<CategoryItem>?> GetCategoriesAsync() => GetListAsync<CategoryItem>("api/categories");

    /// <summary>Active suppliers only (Admin and Kho roles).</summary>
    public Task<List<SupplierItem>?> GetActiveSuppliersAsync() => GetListAsync<SupplierItem>("api/suppliers/active");

    /// <summary>First 100 customers (the API's page-size limit). Null for roles that may not list customers.</summary>
    public async Task<List<CustomerItem>?> GetCustomersAsync()
    {
        try
        {
            var client = _httpClientFactory.CreateClient(ApiClient.Name);
            var response = await client.GetAsync("api/customers?page=1&pageSize=100");
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var page = await response.Content.ReadFromJsonAsync<PagedResponse<CustomerItem>>();
            return page?.Items.ToList();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>Every product, fetched page by page because GET /api/products is paged (max 100 per page).</summary>
    public async Task<List<ProductItem>?> GetProductsAsync()
    {
        try
        {
            var client = _httpClientFactory.CreateClient(ApiClient.Name);
            var all = new List<ProductItem>();
            for (var page = 1; ; page++)
            {
                var response = await client.GetAsync($"api/products?page={page}&pageSize=100");
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var result = await response.Content.ReadFromJsonAsync<PagedResponse<ProductItem>>();
                if (result is null)
                {
                    return null;
                }

                all.AddRange(result.Items);
                if (result.Items.Count == 0 || all.Count >= result.TotalCount)
                {
                    return all;
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private async Task<List<T>?> GetListAsync<T>(string url)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(ApiClient.Name);
            var response = await client.GetAsync(url);
            return response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync<List<T>>()
                : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return null;
        }
    }
}

