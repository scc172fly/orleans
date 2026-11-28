// <configuration>
using Orleans.Runtime;

var builder = WebApplication.CreateBuilder(args);

// <orleans-configuration>
builder.Host.UseOrleans(static siloBuilder =>
{
    siloBuilder.UseLocalhostClustering();
    siloBuilder.AddMemoryGrainStorage("urls");
});
// </orleans-configuration>

using var app = builder.Build();
// </configuration>

// <endpoints>
app.MapGet("/", static () => "Welcome to the URL shortener, powered by Orleans!");

app.MapGet("/shorten",
    static async (IGrainFactory grains, HttpRequest request, string url) =>
    {
        var host = $"{request.Scheme}://{request.Host.Value}";

        // 验证 URL 查询字符串。
        if (string.IsNullOrWhiteSpace(url) ||
            Uri.IsWellFormedUriString(url, UriKind.Absolute) is false)
        {
            return Results.BadRequest($"""
                The URL query string is required and needs to be well formed.
                Consider, ${host}/shorten?url=https://www.microsoft.com.
                """);
        }

        // 创建唯一的短 ID。
        var shortenedRouteSegment = Guid.NewGuid().GetHashCode().ToString("X");

        // 使用缩短后的 ID 和完整 URL 创建并持久化一个 Grain。
        var shortenerGrain =
            grains.GetGrain<IUrlShortenerGrain>(shortenedRouteSegment);

        await shortenerGrain.SetUrl(url);

        // 返回缩短后的 URL，以供后续使用。
        var resultBuilder = new UriBuilder(host)
        {
            Path = $"/go/{shortenedRouteSegment}"
        };

        return Results.Ok(resultBuilder.Uri);
    });

app.MapGet("/go/{shortenedRouteSegment:required}",
    static async (IGrainFactory grains, string shortenedRouteSegment) =>
    {
        // 使用短 ID 获取 Grain，并重定向至原始 URL。
        var shortenerGrain =
            grains.GetGrain<IUrlShortenerGrain>(shortenedRouteSegment);

        var url = await shortenerGrain.GetUrl();

        // 处理缺少协议的情况，默认使用 "http://"。
        var redirectBuilder = new UriBuilder(url);

        return Results.Redirect(redirectBuilder.Uri.ToString());
    });

app.Run();
// </endpoints>

// <graininterface>
public interface IUrlShortenerGrain : IGrainWithStringKey
{
    Task SetUrl(string fullUrl);

    Task<string> GetUrl();
}
// </graininterface>

// <grain>
public sealed class UrlShortenerGrain(
    [PersistentState(
        stateName: "url",
        storageName: "urls")]
        IPersistentState<UrlDetails> state)
    : Grain, IUrlShortenerGrain
{
    public async Task SetUrl(string fullUrl)
    {
        state.State = new()
        {
            ShortenedRouteSegment = this.GetPrimaryKeyString(),
            FullUrl = fullUrl
        };

        await state.WriteStateAsync();
    }

    public Task<string> GetUrl() =>
        Task.FromResult(state.State.FullUrl);
}

[GenerateSerializer, Alias(nameof(UrlDetails))]
public sealed record class UrlDetails
{
    [Id(0)]
    public string FullUrl { get; set; } = "";

    [Id(1)]
    public string ShortenedRouteSegment { get; set; } = "";
}
// </grain>