using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SalesInventory.Application.Dtos;
using SalesInventory.Application.Interfaces;
using SalesInventory.Infrastructure.Storage;

namespace SalesInventory.Api.Tests;

// Image uploads write to a throwaway folder instead of the real wwwroot
public class ImageUploadFactory : CustomWebApplicationFactory
{
    public string WebRoot { get; } = Path.Combine(Path.GetTempPath(), $"img-tests-{Guid.NewGuid():N}");

    public string ProductsDir => Path.Combine(WebRoot, "uploads", "products");

    public int FileCount => Directory.Exists(ProductsDir) ? Directory.GetFiles(ProductsDir).Length : 0;

    protected override void ConfigureExtraSettings(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IFileStorage>();
            services.AddSingleton<IFileStorage>(new LocalFileStorage(WebRoot));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(WebRoot))
        {
            Directory.Delete(WebRoot, recursive: true);
        }
    }
}

public class ProductImageUploadTests : IClassFixture<ImageUploadFactory>
{
    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0 };

    private readonly ImageUploadFactory _factory;

    public ProductImageUploadTests(ImageUploadFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> ClientAsync(string? role = "Kho")
    {
        var client = _factory.CreateClient();
        if (role is not null)
        {
            var token = await AuthTestHelper.RegisterAndLoginAsync(client, role);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    private static MultipartFormDataContent Form(byte[] bytes, string fileName, string contentType)
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { file, "file", fileName } };
    }

    [Fact]
    public async Task ValidPng_Returns200_SavesUnderGuidName_AndUpdatesProduct()
    {
        var client = await ClientAsync();

        var response = await client.PostAsync("/api/products/1/image", Form(Png, "../../evil name.PNG", "image/png"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<ProductImageDto>();
        Assert.Matches(@"^/uploads/products/[0-9a-f]{32}\.png$", dto!.ImageUrl);
        Assert.True(File.Exists(Path.Combine(_factory.ProductsDir, Path.GetFileName(dto.ImageUrl))));

        var product = await client.GetFromJsonAsync<ProductDto>("/api/products/1");
        Assert.Equal(dto.ImageUrl, product!.ImageUrl);
    }

    [Fact]
    public async Task ReplacingImage_DeletesThePreviousFile()
    {
        var client = await ClientAsync();
        var first = await (await client.PostAsync("/api/products/1/image", Form(Png, "a.png", "image/png")))
            .Content.ReadFromJsonAsync<ProductImageDto>();
        var second = await (await client.PostAsync("/api/products/1/image", Form(Png, "b.png", "image/png")))
            .Content.ReadFromJsonAsync<ProductImageDto>();

        Assert.False(File.Exists(Path.Combine(_factory.ProductsDir, Path.GetFileName(first!.ImageUrl))));
        Assert.True(File.Exists(Path.Combine(_factory.ProductsDir, Path.GetFileName(second!.ImageUrl))));
    }

    [Fact]
    public async Task TextFile_Returns400_AndWritesNothing()
    {
        var client = await ClientAsync();
        var before = _factory.FileCount;

        var response = await client.PostAsync("/api/products/1/image", Form("hello"u8.ToArray(), "note.txt", "text/plain"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, _factory.FileCount);
    }

    [Fact]
    public async Task TextRenamedToPng_Returns400_AndWritesNothing()
    {
        var client = await ClientAsync();
        var before = _factory.FileCount;

        var response = await client.PostAsync("/api/products/1/image", Form("not an image at all"u8.ToArray(), "fake.png", "image/png"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, _factory.FileCount);
    }

    [Fact]
    public async Task WrongContentType_Returns400()
    {
        var client = await ClientAsync();

        var response = await client.PostAsync("/api/products/1/image", Form(Png, "a.png", "application/octet-stream"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ImageOver2Mb_Returns400_WithClearMessage_AndWritesNothing()
    {
        var client = await ClientAsync();
        var before = _factory.FileCount;
        var big = new byte[2 * 1024 * 1024 + 1];
        Png.CopyTo(big, 0);

        var response = await client.PostAsync("/api/products/1/image", Form(big, "big.png", "image/png"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("2 MB", await response.Content.ReadAsStringAsync());
        Assert.Equal(before, _factory.FileCount);
    }

    [Fact]
    public async Task UnknownProduct_Returns404()
    {
        var client = await ClientAsync();

        var response = await client.PostAsync("/api/products/999999/image", Form(Png, "a.png", "image/png"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task WithoutToken_Returns401()
    {
        var client = await ClientAsync(role: null);

        var response = await client.PostAsync("/api/products/1/image", Form(Png, "a.png", "image/png"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SalesRole_Returns403()
    {
        var client = await ClientAsync("BanHang");

        var response = await client.PostAsync("/api/products/1/image", Form(Png, "a.png", "image/png"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
