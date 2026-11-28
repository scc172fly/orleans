using Microsoft.Extensions.Hosting;

namespace Orleans.Docs.Snippets.Aspire.Client;

// 此类包含结合 Aspire 配置 Orleans 客户端的示例代码。
public static class ClientProgram
{
    // <client_basic_config>
    public static void BasicClientConfiguration(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.AddServiceDefaults();
        builder.AddKeyedRedisClient("orleans-redis");

        // 配置 Orleans 客户端：Aspire 自动注入集群成员管理配置。
        builder.UseOrleansClient();

        builder.Build().Run();
    }
    // </client_basic_config>
}
