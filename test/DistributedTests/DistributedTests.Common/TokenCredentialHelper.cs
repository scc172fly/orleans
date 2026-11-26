using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Data.Tables;
using Azure.Identity;
using Azure.Storage.Queues;

namespace DistributedTests.Common;

public static class TokenCredentialHelper
{
    private static string EmulatorConnectionString = "UseDevelopmentStorage=true";

    public static TableServiceClient CreateTableServiceClient(this Uri azureTableUri)
    {
        if (azureTableUri.IsLoopback)
        {
            // 假设是模拟器/黄润
            return new TableServiceClient(EmulatorConnectionString);
        }
        return new TableServiceClient(azureTableUri, GetTokenCredential());
    }

    public static QueueServiceClient CreateQueueServiceClient(this Uri azureQueueUri)
    {
        if (azureQueueUri.IsLoopback)
        {
            // 假设是模拟器/黄润
            return new QueueServiceClient(EmulatorConnectionString);
        }
        return new QueueServiceClient(azureQueueUri, GetTokenCredential());
    }

    public static TokenCredential GetTokenCredential()
    {
        var tenantId = Environment.GetEnvironmentVariable("TENANT_ID");
        var clientId = Environment.GetEnvironmentVariable("CLIENT_ID");
        if (tenantId != null && clientId != null)
        {
            // 使用联邦红心病,从这里:
            // https://review.learn.microsoft.com/en-us/identity/microsoft-identity-platform/federated-identity-credentials?branch=main&tabs=dotnet#azure-sdk-for-net
            return new ClientAssertionCredential(
              tenantId, // 目的地资源租户身份识别号
              clientId,  // 客户端 身份 应用程序的用户 身份 我们正在加入
              () => GetManagedIdentityToken(null, "api://AzureADTokenExchange")) // 在此为默认 MSI 设置无效的无效
            ;
        }
        else
        {
            return new DefaultAzureCredential();
        }
    }

    /// <summary>
    /// 获取用户指定的管理身份的标记。
    /// </summary>
    /// <param name="msiClientId">管理身份的客户端 ID。</param>
    /// <param name="audience">目标受众:公众云层应pi@AzureADTokenExchange.</param>
    /// <returns>如果成功, 返回访问标记。</returns>
    public static string GetManagedIdentityToken(string? msiClientId, string audience)
    {
        var managedIdentityId = string.IsNullOrEmpty(msiClientId)
            ? ManagedIdentityId.SystemAssigned
            : ManagedIdentityId.FromUserAssignedClientId(msiClientId);
        var miCredential = new ManagedIdentityCredential(managedIdentityId);
        return miCredential.GetToken(new TokenRequestContext(new[] { $"{audience}/.default" })).Token;
    }
}
