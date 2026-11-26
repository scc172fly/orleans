using System;
using System.Collections.Frozen;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace Orleans.Dashboard;

internal sealed class EmbeddedAssetProvider
{
    private const string GZipEncodingValue = "gzip";
    private static readonly StringValues GzipEncodingHeader = new(GZipEncodingValue);
    private static readonly Assembly Assembly = typeof(EmbeddedAssetProvider).Assembly;
    private static readonly FileExtensionContentTypeProvider ContentTypeProvider = new();
    private static readonly StringValues CacheControl = new(new CacheControlHeaderValue()
    {
        NoCache = true,
        NoStore = true,
    }.ToString());

    private readonly FrozenDictionary<string, ResourceEntry> _resourceCache;

    public EmbeddedAssetProvider()
    {
        // 为所有嵌入资源构建资源缓存
        var resourceNamePrefix = $"{Assembly.GetName().Name}.wwwroot.";
        _resourceCache = Assembly
            .GetManifestResourceNames()
            .Where(p => p.StartsWith(resourceNamePrefix, StringComparison.Ordinal))
            .ToFrozenDictionary(
                p => p[resourceNamePrefix.Length..],
                CreateResourceEntry,
                StringComparer.OrdinalIgnoreCase);
    }

    public IResult ServeAsset(string name, HttpContext context)
    {
        // 嵌入资源使用点而不是用于目录分隔符的斜线
        var resourceKey = name.Replace('/', '.');

        if (!_resourceCache.TryGetValue(resourceKey, out var entry))
        {
            return Results.NotFound();
        }

        // 检查客户端是否已缓存版本
        if (context.Request.Headers.IfNoneMatch == entry.ETag)
        {
            return Results.StatusCode(StatusCodes.Status304NotModified);
        }

        byte[] contents;
        var response = context.Response;
        response.Headers.CacheControl = CacheControl;
        if (entry.CompressedContent is not null && IsGZipAccepted(context.Request))
        {
            response.Headers.ContentEncoding = GzipEncodingHeader;
            contents = entry.CompressedContent;
        }
        else
        {
            contents = entry.DecompressedContent;
        }

        return Results.Bytes(
            contents,
            contentType: entry.ContentType,
            entityTag: new EntityTagHeaderValue(entry.ETag));
    }

    private static bool IsGZipAccepted(HttpRequest httpRequest)
    {
        if (httpRequest.GetTypedHeaders().AcceptEncoding is not { Count: > 0 } acceptEncoding)
        {
            return false;
        }

        for (int i = 0; i < acceptEncoding.Count; i++)
        {
            var encoding = acceptEncoding[i];
            if (encoding.Quality is not 0 &&
                string.Equals(encoding.Value.Value, GZipEncodingValue, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static ResourceEntry CreateResourceEntry(string fullResourceName)
    {
        using var resourceStream = Assembly.GetManifestResourceStream(fullResourceName) ?? throw new FileNotFoundException($"Embedded resource not found: {fullResourceName}");
        using var decompressedContent = new MemoryStream();
        resourceStream.CopyTo(decompressedContent);
        var decompressedArray = decompressedContent.ToArray();

        // 压缩内容
        using var compressedContent = new MemoryStream();
        using (var gzip = new GZipStream(compressedContent, CompressionMode.Compress, leaveOpen: true))
        {
            gzip.Write(decompressedArray);
        }

        // 只有实际压缩大小时才使用压缩
        byte[]? compressedArray = compressedContent.Length < decompressedArray.Length
            ? compressedContent.ToArray()
            : null;

        var hash = SHA256.HashData(compressedArray ?? decompressedArray);
        var eTag = $"\"{Convert.ToBase64String(hash)}\"";

        // 仅提取带有内容类型探测扩展名的文件名
        var fileName = fullResourceName.Split('.').TakeLast(2).FirstOrDefault() + "." + fullResourceName.Split('.').Last();
        var contentType = ContentTypeProvider.TryGetContentType(fileName, out var ct)
            ? ct
            : "application/octet-stream";

        return new ResourceEntry(decompressedArray, compressedArray, eTag, contentType);
    }

    private sealed class ResourceEntry(byte[] decompressedContent, byte[]? compressedContent, string eTag, string contentType)
    {
        public byte[]? CompressedContent { get; } = compressedContent;
        public string ContentType { get; } = contentType;
        public byte[] DecompressedContent { get; } = decompressedContent;
        public string ETag { get; } = eTag;
    }
}
