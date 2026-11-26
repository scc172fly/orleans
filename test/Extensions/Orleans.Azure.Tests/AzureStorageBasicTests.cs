namespace Tester.AzureUtils
{
    /// <summary>
    /// 由于 https://github.com/AArnott/Xunit.SkippableFact/issues/32，请勿将此类用作 <code>IClassFixture</code>。
    /// </summary>
    public abstract class AzureStorageBasicTests
    {
        public AzureStorageBasicTests()
        {
            TestUtils.CheckForAzureStorage();
        }
    }
}
