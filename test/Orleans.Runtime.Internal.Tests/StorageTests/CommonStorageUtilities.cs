using Xunit;


namespace UnitTests.StorageTests.Relational
{
    /// <summary>
    /// 测试存储提供程序时使用的通用辅助工具。
    /// </summary>
    public static class CommonStorageUtilities
    {
        /// <summary>
        /// 断言 <see cref="Orleans.Storage.InconsistentStateException"/> 中包含特定信息。
        /// </summary>
        /// <param name="exceptionMessage">要进行断言的异常消息。</param>
        public static void AssertRelationalInconsistentExceptionMessage(string exceptionMessage)
        {
            Assert.Contains("ServiceId=", exceptionMessage);
            Assert.Contains("ProviderName=", exceptionMessage);
            Assert.Contains("GrainType=", exceptionMessage);
            Assert.Contains("GrainId=", exceptionMessage);
            Assert.Contains("ETag=", exceptionMessage);
        }
    }
}
