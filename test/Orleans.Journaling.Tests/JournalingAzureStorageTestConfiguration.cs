using TestExtensions;
using Xunit;

namespace Orleans.Durable.Journaling.Tests;

internal static class JournalingAzureStorageTestConfiguration
{
    public static void CheckPreconditionsOrThrow()
    {
        if (TestDefaultConfiguration.UseAadAuthentication)
        {
            Assert.SkipWhen(string.IsNullOrEmpty(TestDefaultConfiguration.DataBlobUri.ToString()), "DataBlobUri is not set. Skipping test.");
        }
        else
        {
            Assert.SkipWhen(string.IsNullOrEmpty(TestDefaultConfiguration.DataConnectionString), "DataConnectionString is not set. Skipping test.");
        }
    }

    public static AzureBlobJournalStorageOptions ConfigureTestDefaults(this AzureBlobJournalStorageOptions options)
    {
        if (TestDefaultConfiguration.UseAadAuthentication)
        {
            options.ConfigureBlobServiceClient(TestDefaultConfiguration.DataBlobUri, TestDefaultConfiguration.TokenCredential);
        }
        else
        {
            options.ConfigureBlobServiceClient(TestDefaultConfiguration.DataConnectionString!); // Azure Storage 测试初始化会提供连接字符串。
        }

        return options;
    }

    public static AzureTableJournalStorageOptions ConfigureTestDefaults(this AzureTableJournalStorageOptions options)
    {
        if (TestDefaultConfiguration.UseAadAuthentication)
        {
            options.ConfigureTableServiceClient(TestDefaultConfiguration.TableEndpoint, TestDefaultConfiguration.TokenCredential);
        }
        else
        {
            options.ConfigureTableServiceClient(TestDefaultConfiguration.DataConnectionString!);
        }

        return options;
    }
}
