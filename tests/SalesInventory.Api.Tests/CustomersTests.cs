using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SalesInventory.Application.Dtos;

namespace SalesInventory.Api.Tests;

// Customer CRUD: validation (400 ProblemDetails), paging, update and delete
public class CustomersTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public CustomersTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Create_MissingName_Returns400ProblemDetails()
    {
        var client = await AdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/customers", new { phone = "0900000000" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.GetProperty("errors").TryGetProperty("Name", out _));
    }

    [Fact]
    public async Task Create_TooLongPhone_Returns400()
    {
        var client = await AdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/customers", new { name = "A", phone = new string('1', 21) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Crud_Flow_Works()
    {
        var client = await AdminClientAsync();

        var create = await client.PostAsJsonAsync("/api/customers", new { name = "Nguyen Van A", phone = "0911111111", email = "a@test.local", address = "HN" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<CustomerDto>();
        Assert.NotEqual(default, created!.CreatedAt);

        var get = await client.GetFromJsonAsync<CustomerDto>($"/api/customers/{created.Id}");
        Assert.Equal("Nguyen Van A", get!.Name);

        var put = await client.PutAsJsonAsync($"/api/customers/{created.Id}", new { name = "Nguyen Van B" });
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        Assert.Equal("Nguyen Van B", (await client.GetFromJsonAsync<CustomerDto>($"/api/customers/{created.Id}"))!.Name);

        var page = await client.GetFromJsonAsync<PagedResult<CustomerDto>>("/api/customers?page=1&pageSize=5");
        Assert.Equal(1, page!.Page);
        Assert.True(page.Items.Count is >= 1 and <= 5);
        Assert.True(page.TotalCount >= 1);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/customers/{created.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/customers/{created.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/customers/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task List_InvalidPaging_Returns400()
    {
        var client = await AdminClientAsync();

        var response = await client.GetAsync("/api/customers?page=0&pageSize=10");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<HttpClient> AdminClientAsync()
    {
        var client = _factory.CreateClient();
        var token = await AuthTestHelper.RegisterAndLoginAsync(client, "Admin");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
