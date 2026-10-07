using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using SalesInventory.Application.Interfaces;
using SalesInventory.Application.Services;

namespace SalesInventory.Infrastructure.Storage;

// Downloads an image from a user-supplied URL without letting the server be used to reach internal networks (SSRF):
//  - https on port 443 only, no credentials in the URL;
//  - the address check happens at connect time, on the IPs actually used, so DNS rebinding cannot swap in an internal IP;
//  - redirects are followed by hand (max 3) and every hop goes through the same checks;
//  - no proxy (it would connect on our behalf and bypass the check), hard timeouts and a byte limit.
public sealed class SafeImageDownloader : IRemoteImageDownloader, IDisposable
{
    private const int MaxRedirects = 3;
    private const int HttpsPort = 443;

    private readonly HttpClient _client;

    public SafeImageDownloader()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            UseCookies = false,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            ConnectCallback = ConnectToPublicAddressAsync
        };

        _client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
    }

    public async Task<DownloadedImage> DownloadAsync(Uri url, long maxBytes, CancellationToken cancellationToken = default)
    {
        var current = url;

        for (var hop = 0; hop <= MaxRedirects; hop++)
        {
            ValidateUrl(current);

            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.Accept.ParseAdd("image/jpeg, image/png, image/webp");
            request.Headers.UserAgent.ParseAdd("SalesInventory/1.0");

            HttpResponseMessage response;
            try
            {
                response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            }
            catch (HttpRequestException ex) when (ex.InnerException is BlockedAddressException)
            {
                throw new ImageDownloadException("Địa chỉ ảnh không được phép (địa chỉ nội bộ hoặc không hợp lệ).");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
            {
                throw new ImageDownloadException("Không tải được ảnh từ địa chỉ này (không kết nối được hoặc quá thời gian).", isUpstreamError: true, inner: ex);
            }

            using (response)
            {
                if (IsRedirect(response.StatusCode))
                {
                    var location = response.Headers.Location;
                    if (location is null)
                    {
                        throw new ImageDownloadException("Máy chủ ảnh chuyển hướng không hợp lệ.", isUpstreamError: true);
                    }

                    current = location.IsAbsoluteUri ? location : new Uri(current, location);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new ImageDownloadException($"Máy chủ ảnh trả về lỗi {(int)response.StatusCode}.", isUpstreamError: true);
                }

                return await ReadBodyAsync(response, maxBytes, cancellationToken);
            }
        }

        throw new ImageDownloadException("Địa chỉ ảnh chuyển hướng quá nhiều lần.");
    }

    private static async Task<DownloadedImage> ReadBodyAsync(HttpResponseMessage response, long maxBytes, CancellationToken cancellationToken)
    {
        var contentType = response.Content.Headers.ContentType?.MediaType;
        if (!ProductImageRules.IsAllowedContentType(contentType))
        {
            throw new ImageDownloadException("Địa chỉ này không trả về ảnh JPEG, PNG hoặc WebP.");
        }

        var tooLarge = new ImageDownloadException("Ảnh vượt quá dung lượng tối đa 2 MB.");
        if (response.Content.Headers.ContentLength > maxBytes)
        {
            throw tooLarge;
        }

        try
        {
            // Content-Length can lie or be absent, so the limit is enforced while reading
            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = await body.ReadAsync(chunk, cancellationToken)) > 0)
            {
                if (buffer.Length + read > maxBytes)
                {
                    throw tooLarge;
                }

                buffer.Write(chunk, 0, read);
            }

            return new DownloadedImage(buffer.ToArray(), contentType!);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            throw new ImageDownloadException("Không tải được ảnh từ địa chỉ này (kết nối bị ngắt hoặc quá thời gian).", isUpstreamError: true, inner: ex);
        }
    }

    private static bool IsRedirect(HttpStatusCode status) =>
        status is HttpStatusCode.Moved or HttpStatusCode.Found or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    private static void ValidateUrl(Uri url)
    {
        if (!url.IsAbsoluteUri
            || url.Scheme != Uri.UriSchemeHttps
            || url.Port != HttpsPort
            || !string.IsNullOrEmpty(url.UserInfo)
            || string.IsNullOrEmpty(url.Host))
        {
            throw new ImageDownloadException("Chỉ chấp nhận địa chỉ ảnh dạng https://... (cổng 443, không kèm tài khoản).");
        }
    }

    // Resolves the host itself and connects straight to a validated IP, so what is checked is exactly what is used
    private static async ValueTask<Stream> ConnectToPublicAddressAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        // An IP literal needs no lookup (and Dns rejects 0.0.0.0 / :: with an exception instead of returning them)
        var host = context.DnsEndPoint.Host;
        var addresses = IPAddress.TryParse(host, out var literal)
            ? new[] { literal }
            : await Dns.GetHostAddressesAsync(host, cancellationToken);
        if (addresses.Length == 0 || addresses.Any(IsBlockedAddress))
        {
            // A host that resolves to anything internal is refused outright, even if it also has public records
            throw new BlockedAddressException();
        }

        Exception? lastError = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception ex) when (ex is SocketException)
            {
                socket.Dispose();
                lastError = ex;
            }
        }

        throw new HttpRequestException("Could not connect to the image host.", lastError);
    }

    // True for every address that is not a normal public internet address
    public static bool IsBlockedAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        var b = address.GetAddressBytes();

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return b[0] == 0                                   // 0.0.0.0/8 "this network"
                || b[0] == 10                                  // private
                || (b[0] == 100 && (b[1] & 0xC0) == 64)        // 100.64.0.0/10 carrier-grade NAT
                || (b[0] == 169 && b[1] == 254)                // link-local, incl. cloud metadata 169.254.169.254
                || (b[0] == 172 && (b[1] & 0xF0) == 16)        // 172.16.0.0/12 private
                || (b[0] == 192 && b[1] == 0 && b[2] == 0)     // 192.0.0.0/24 protocol assignments
                || (b[0] == 192 && b[1] == 168)                // private
                || (b[0] == 198 && (b[1] & 0xFE) == 18)        // 198.18.0.0/15 benchmarking
                || b[0] >= 224;                                // multicast, reserved, broadcast
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return address.Equals(IPAddress.IPv6None)
                || address.Equals(IPAddress.IPv6Any)
                || address.IsIPv6LinkLocal
                || address.IsIPv6SiteLocal
                || address.IsIPv6Multicast
                || (b[0] & 0xFE) == 0xFC                       // fc00::/7 unique local
                || (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0 && b[3] == 0)  // 2001::/32 Teredo (embeds IPv4)
                || (b[0] == 0x20 && b[1] == 0x02)              // 2002::/16 6to4 (embeds IPv4)
                || (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xFF && b[3] == 0x9B); // 64:ff9b::/96 NAT64
        }

        return true;
    }

    public void Dispose() => _client.Dispose();

    // Thrown from the connect callback; HttpClient wraps it in an HttpRequestException
    private sealed class BlockedAddressException : Exception
    {
    }
}
