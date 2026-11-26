using Xunit;


namespace UnitTests.StorageTests.Relational
{
    /// <summary>
    /// 测试储存提供者时使用通用公用设施。
    /// </summary>
    public static class CommonStorageUtilities
    {
        /// <summary>
        /// 发信确认 <see cref="Orleans.Storage.InconsistentStateException"/>中存在某些信息。
        /// </summary>
        /// <param name="exceptionMessage">要求声明的例外电文。</param>
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
