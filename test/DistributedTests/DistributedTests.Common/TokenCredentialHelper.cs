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
            // 假定使用模拟器或 Azurite
            return new TableServiceClient(EmulatorConnectionString);
        }
        return new TableServiceClient(azureTableUri, GetTokenCredential());
    }

    public static QueueServiceClient CreateQueueServiceClient(this Uri azureQueueUri)
    {
        if (azureQueueUri.IsLoopback)
        {
            // 假定使用模拟器或 Azurite
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
            // 使用联合身份凭据，来源如下：
            // https://review.learn.microsoft.com/en-us/identity/microsoft-identity-platform/federated-identity-credentials?branch=main&tabs=dotnet#azure-sdk-for-net
            return new ClientAssertionCredential(
              tenantId, // 目标资源的租户 ID
              clientId,  // 要建立联合身份关系的应用程序的客户端 ID
              () => GetManagedIdentityToken(null, "api://AzureADTokenExchange")) // 此处为 null 时使用默认的 MSI
            ;
        }
        else
        {
            return new DefaultAzureCredential();
        }
    }

    /// <summary>
    /// 获取用户分配的托管标识的令牌。
    /// </summary>
    /// <param name="msiClientId">托管标识的客户端 ID。</param>
    /// <param name="audience">目标受众。对于公有云，应为 api://AzureADTokenExchange。</param>
    /// <returns>成功时返回访问令牌。</returns>
    public static string GetManagedIdentityToken(string? msiClientId, string audience)
    {
        var managedIdentityId = string.IsNullOrEmpty(msiClientId)
            ? ManagedIdentityId.SystemAssigned
            : ManagedIdentityId.FromUserAssignedClientId(msiClientId);
        var miCredential = new ManagedIdentityCredential(managedIdentityId);
        return miCredential.GetToken(new TokenRequestContext(new[] { $"{audience}/.default" })).Token;
    }
}
