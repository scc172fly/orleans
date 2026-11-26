namespace Orleans.Transactions.TestKit
{
    /// <summary>
    /// 定义交易测试 grains所用的整数状态。
    /// </summary>
    public interface ITestState
    {
        /// <summary>
        /// 获取或设置当前测试值。
        /// </summary>
        int state { get; set; }
    }
}
