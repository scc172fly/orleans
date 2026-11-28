using System.Net;
using Orleans.Persistence.AzureStorage;
using Xunit;

namespace Tester.AzureUtils
{
    /// <summary>
    /// 测试 Azure Storage 错误码处理以及表名和容器名的验证。
    /// </summary>
    [TestCategory("AzureStorage"), TestCategory("Storage")]
    [TestSuite("Functional")]
    [TestProvider("AzureStorage")]
    [TestArea("Persistence")]
    public class AzureTableErrorCodeTests
    {

        [Fact, TestCategory("Functional")]
        public void AzureTableErrorCode_IsRetriableHttpError()
        {
            Assert.True(AzureTableUtils.IsRetriableHttpError((HttpStatusCode)503, null));
            Assert.True(AzureTableUtils.IsRetriableHttpError((HttpStatusCode)504, null));
            Assert.True(AzureTableUtils.IsRetriableHttpError((HttpStatusCode)408, null));

            Assert.True(AzureTableUtils.IsRetriableHttpError((HttpStatusCode)500, "OperationTimedOut"));
            Assert.False(AzureTableUtils.IsRetriableHttpError((HttpStatusCode)500, null));
            Assert.False(AzureTableUtils.IsRetriableHttpError((HttpStatusCode)500, "SomeOtherStatusValue"));

            // 当前行为是忽略成功结果，将其视为无需重试：
            Assert.False(AzureTableUtils.IsRetriableHttpError((HttpStatusCode)200, null));
        }

        [Fact, TestCategory("Functional")]
        public void AzureTableErrorCode_IsContentionError()
        {
            Assert.True(AzureTableUtils.IsContentionError(HttpStatusCode.PreconditionFailed));
            Assert.True(AzureTableUtils.IsContentionError(HttpStatusCode.Conflict));
            Assert.True(AzureTableUtils.IsContentionError(HttpStatusCode.NotFound));
            Assert.True(AzureTableUtils.IsContentionError(HttpStatusCode.NotImplemented));

            Assert.False(AzureTableUtils.IsContentionError((HttpStatusCode)503));
            Assert.False(AzureTableUtils.IsContentionError((HttpStatusCode)504));
            Assert.False(AzureTableUtils.IsContentionError((HttpStatusCode)408));
            Assert.False(AzureTableUtils.IsContentionError((HttpStatusCode)500));
            Assert.False(AzureTableUtils.IsContentionError((HttpStatusCode)500));
            Assert.False(AzureTableUtils.IsContentionError((HttpStatusCode)500));
            Assert.False(AzureTableUtils.IsContentionError((HttpStatusCode)200));
        }

        [Fact, TestCategory("Functional")]
        public void AzureTableErrorCode_BadTableName()
        {

            string tableName = "abc-123";
            Assert.Throws<ArgumentException>(() =>
            AzureTableUtils.ValidateTableName(tableName));
        }

        [Fact, TestCategory("Functional")]
        public void AzureStorageUtils_ContainerName()
        {
            Assert.Throws<ArgumentException>(() => AzureBlobUtils.ValidateContainerName("this is a test"));
            Assert.Throws<ArgumentException>(() => AzureBlobUtils.ValidateContainerName("MyContainer"));
            Assert.Throws<ArgumentException>(() => AzureBlobUtils.ValidateContainerName("my_container123"));
            Assert.Throws<ArgumentException>(() => AzureBlobUtils.ValidateContainerName("_container"));
            Assert.Throws<ArgumentException>(() => AzureBlobUtils.ValidateContainerName("__"));
            Assert.Throws<ArgumentException>(() => AzureBlobUtils.ValidateContainerName("_."));
            AzureBlobUtils.ValidateContainerName("123");
            AzureBlobUtils.ValidateContainerName("container");
            AzureBlobUtils.ValidateContainerName("my-container123");
        }

        [Fact, TestCategory("Functional")]
        public void AzureStorageUtils_BlobName()
        {
            Assert.Throws<ArgumentException>(() => AzureBlobUtils.ValidateBlobName(""));
            Assert.Throws<ArgumentException>(() => AzureBlobUtils.ValidateBlobName(" "));
            AzureBlobUtils.ValidateBlobName(".");
            AzureBlobUtils.ValidateBlobName("/");
            AzureBlobUtils.ValidateContainerName("123");
            AzureBlobUtils.ValidateContainerName("orleans-blob");
        }

        [Fact, TestCategory("Functional")]
        public void AzureStorageUtils_TablePropertyShouldBeSanitized()
        {
            var tableProperty = "/A\\C#?";
            Assert.Equal("_A_C__", AzureTableUtils.SanitizeTableProperty(tableProperty));
        }
    }
}
