using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SalesInventory.Application.Dtos;
using SalesInventory.Application.Interfaces;
using SalesInventory.Application.Services;
using SalesInventory.Infrastructure.Storage;

namespace SalesInventory.Api.Tests;

// Stands in for Open Food Facts so tests never touch the network
internal sealed class FakeImageLookup : IProductImageLookup
{
    public const string KnownBarcode = "8934563138165";
    public const string FakeImageBarcode = "8934563138172";
    public const string DownBarcode = "8934563138189";

    public Task<Uri?> FindByBarcodeAsync(string barcode, CancellationToken cancellationToken = default) => barcode switch
    {
        KnownBarcode => Task.FromResult<Uri?>(new Uri("https://images.test/ok.png")),
        FakeImageBarcode => Task.FromResult<Uri?>(new Uri("https://images.test/fake.png")),
        DownBarcode => throw new ImageDownloadException("Open Food Facts down", isUpstreamError: true),
        _ => Task.FromResult<Uri?>(null)
    };
}

// Serves canned responses for the made-up host "images.test"; every other URL goes through the real SSRF-safe downloader
internal sealed class FakeImageDownloader : IRemoteImageDownloader
{
    public static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0 };

    private readonly SafeImageDownloader _real = new();

    public Task<DownloadedImage> DownloadAsync(Uri url, long maxBytes, CancellationToken cancellationToken = default)
    {
        if (url.Host != "images.test")
        {
            return _real.DownloadAsync(url, maxBytes, cancellationToken);
        }

        return url.AbsolutePath switch
        {
            "/ok.png" => Task.FromResult(new DownloadedImage(Png, "image/png")),
            "/fake.png" => Task.FromResult(new DownloadedImage("<html>not an image</html>"u8.ToArray(), "image/png")),
            "/down" => throw new ImageDownloadException("unreachable", isUpstreamError: true),
            _ => throw new ImageDownloadException("Ảnh vượt quá dung lượng tối đa 2 MB.")
        };
    }
}

public class InternetImageFactory : ImageUploadFactory
{
    protected override void ConfigureExtraSettings(IWebHostBuilder builder)
    {
        base.ConfigureExtraSettings(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IRemoteImageDownloader>();
            services.AddSingleton<IRemoteImageDownloader, FakeImageDownloader>();
            services.RemoveAll<IProductImageLookup>();
            services.AddSingleton<IProductImageLookup, FakeImageLookup>();
        });
    }
}

public class ProductImageFromInternetTests : IClassFixture<InternetImageFactory>
{
    private readonly InternetImageFactory _factory;

    public ProductImageFromInternetTests(InternetImageFactory factory)
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

    private static async Task<int> CreateProductAsync(HttpClient client, string? barcode)
    {
        var response = await client.PostAsJsonAsync("/api/products", new CreateProductDto
        {
            Name = "Net Image", Sku = $"NI-{Guid.NewGuid():N}"[..20].ToUpperInvariant(), Barcode = barcode,
            Unit = "cái", PurchasePrice = 100, SalePrice = 200, Quantity = 1, CategoryId = 1
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ProductDto>())!.Id;
    }

    private static Task<HttpResponseMessage> FromUrl(HttpClient client, int id, string url) =>
        client.PostAsJsonAsync($"/api/products/{id}/image-from-url", new ImageFromUrlDto { Url = url });

    // ---- image-from-url ----

    [Fact]
    public async Task FromUrl_ValidImage_Returns200_AndSavesFileUnderGuidName()
    {
        var client = await ClientAsync();
        var id = await CreateProductAsync(client, null);

        var response = await FromUrl(client, id, "https://images.test/ok.png");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<ProductImageDto>();
        Assert.Matches(@"^/uploads/products/[0-9a-f]{32}\.png$", dto!.ImageUrl);
        Assert.True(File.Exists(Path.Combine(_factory.ProductsDir, Path.GetFileName(dto.ImageUrl))));
        var product = await client.GetFromJsonAsync<ProductDto>($"/api/products/{id}");
        Assert.Equal(dto.ImageUrl, product!.ImageUrl);
    }

    [Fact]
    public async Task FromUrl_BodyThatIsNotAnImage_Returns400_AndWritesNothing()
    {
        var client = await ClientAsync();
        var id = await CreateProductAsync(client, null);
        var before = _factory.FileCount;

        var response = await FromUrl(client, id, "https://images.test/fake.png");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, _factory.FileCount);
    }

    [Fact]
    public async Task FromUrl_TooLarge_Returns400_AndWritesNothing()
    {
        var client = await ClientAsync();
        var id = await CreateProductAsync(client, null);
        var before = _factory.FileCount;

        var response = await FromUrl(client, id, "https://images.test/big.png");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("2 MB", await response.Content.ReadAsStringAsync());
        Assert.Equal(before, _factory.FileCount);
    }

    [Fact]
    public async Task FromUrl_RemoteServerDown_Returns502()
    {
        var client = await ClientAsync();
        var id = await CreateProductAsync(client, null);

        var response = await FromUrl(client, id, "https://images.test/down");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    // SSRF: these go through the REAL downloader and must be refused before any connection is made
    [Theory]
    [InlineData("http://example.com/a.png")]                   // not https
    [InlineData("https://example.com:8443/a.png")]             // not port 443
    [InlineData("https://user:pass@example.com/a.png")]        // credentials in URL
    [InlineData("https://127.0.0.1/a.png")]                    // loopback
    [InlineData("https://localhost/a.png")]                    // resolves to loopback
    [InlineData("https://[::1]/a.png")]                        // IPv6 loopback
    [InlineData("https://10.0.0.5/a.png")]                     // private
    [InlineData("https://192.168.1.1/a.png")]                  // private
    [InlineData("https://172.16.0.1/a.png")]                   // private
    [InlineData("https://169.254.169.254/latest/meta-data")]   // cloud metadata
    [InlineData("https://0.0.0.0/a.png")]
    [InlineData("https://[::ffff:127.0.0.1]/a.png")]           // IPv4-mapped loopback
    [InlineData("ftp://example.com/a.png")]
    [InlineData("file:///C:/Windows/win.ini")]
    public async Task FromUrl_InternalOrUnsafeAddress_Returns400_AndWritesNothing(string url)
    {
        var client = await ClientAsync();
        var id = await CreateProductAsync(client, null);
        var before = _factory.FileCount;

        var response = await FromUrl(client, id, url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, _factory.FileCount);
    }

    [Fact]
    public async Task FromUrl_GarbageUrl_Returns400()
    {
        var client = await ClientAsync();
        var id = await CreateProductAsync(client, null);

        Assert.Equal(HttpStatusCode.BadRequest, (await FromUrl(client, id, "not a url")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await FromUrl(client, id, "")).StatusCode);
    }

    [Fact]
    public async Task FromUrl_UnknownProduct_Returns404()
    {
        var client = await ClientAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await FromUrl(client, 999999, "https://images.test/ok.png")).StatusCode);
    }

    [Fact]
    public async Task FromUrl_WithoutToken_Returns401_AndSalesRoleGets403()
    {
        var anonymous = await ClientAsync(role: null);
        var sales = await ClientAsync("BanHang");

        Assert.Equal(HttpStatusCode.Unauthorized, (await FromUrl(anonymous, 1, "https://images.test/ok.png")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await FromUrl(sales, 1, "https://images.test/ok.png")).StatusCode);
    }

    // ---- image-from-barcode ----

    [Fact]
    public async Task FromBarcode_KnownBarcode_Returns200_AndSetsImage()
    {
        var client = await ClientAsync();
        var id = await CreateProductAsync(client, FakeImageLookup.KnownBarcode);

        var response = await client.PostAsync($"/api/products/{id}/image-from-barcode", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<ProductImageDto>();
        Assert.True(File.Exists(Path.Combine(_factory.ProductsDir, Path.GetFileName(dto!.ImageUrl))));
    }

    [Fact]
    public async Task FromBarcode_NoBarcodeOnProduct_Returns400()
    {
        var client = await ClientAsync();
        var id = await CreateProductAsync(client, null);

        var response = await client.PostAsync($"/api/products/{id}/image-from-barcode", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task FromBarcode_NothingOnline_Returns404()
    {
        var client = await ClientAsync();
        var id = await CreateProductAsync(client, "1234567890123");

        var response = await client.PostAsync($"/api/products/{id}/image-from-barcode", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task FromBarcode_LookupServiceDown_Returns502()
    {
        var client = await ClientAsync();
        var id = await CreateProductAsync(client, FakeImageLookup.DownBarcode);

        var response = await client.PostAsync($"/api/products/{id}/image-from-barcode", null);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact]
    public async Task FromBarcode_LookupReturnsNonImage_Returns400_AndWritesNothing()
    {
        var client = await ClientAsync();
        var id = await CreateProductAsync(client, FakeImageLookup.FakeImageBarcode);
        var before = _factory.FileCount;

        var response = await client.PostAsync($"/api/products/{id}/image-from-barcode", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, _factory.FileCount);
    }

    [Fact]
    public async Task FromBarcode_UnknownProduct_Returns404_AndSalesRoleGets403()
    {
        var client = await ClientAsync();
        var sales = await ClientAsync("BanHang");

        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/api/products/999999/image-from-barcode", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await sales.PostAsync("/api/products/1/image-from-barcode", null)).StatusCode);
    }
}

// The address classifier behind the SSRF protection
public class SafeImageDownloaderAddressTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.255.255.254")]
    [InlineData("0.0.0.0")]
    [InlineData("10.1.2.3")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.0.1")]
    [InlineData("169.254.169.254")]
    [InlineData("100.64.0.1")]
    [InlineData("100.127.255.255")]
    [InlineData("198.18.0.1")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("::1")]
    [InlineData("::")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:10.0.0.1")]
    [InlineData("fe80::1")]
    [InlineData("fc00::1")]
    [InlineData("fd12:3456::1")]
    [InlineData("ff02::1")]
    [InlineData("2002:7f00:1::1")]
    [InlineData("64:ff9b::7f00:1")]
    public void InternalAddresses_AreBlocked(string ip) =>
        Assert.True(SafeImageDownloader.IsBlockedAddress(IPAddress.Parse(ip)), ip);

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("172.15.255.255")]
    [InlineData("172.32.0.1")]
    [InlineData("100.63.255.255")]
    [InlineData("100.128.0.1")]
    [InlineData("93.184.216.34")]
    [InlineData("2606:4700:4700::1111")]
    [InlineData("2a00:1450:4001:81b::200e")]
    public void PublicAddresses_AreAllowed(string ip) =>
        Assert.False(SafeImageDownloader.IsBlockedAddress(IPAddress.Parse(ip)), ip);
}
