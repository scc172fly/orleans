namespace DefaultCluster.Tests
{
    /// <summary>
    /// 用于错误处理测试的测试 grain 执行。
    /// 这套 grain 模拟各种错误条件,以测试例外传播和超时情景。
    /// 不是真的 Orleans grain - 用作本地测试错误案例的模拟。
    /// </summary>
    internal class LocalErrorGrain
    {
        private int m_a = 0;
        private int m_b = 0;

        public LocalErrorGrain() { }

        public Task SetA(int a)
        {
            m_a = a;
            return Task.CompletedTask;
        }

        public Task SetB(int b)
        {
            m_b = b;
            return Task.CompletedTask;
        }

        public Task<int> GetAxB()
        {
            return Task.FromResult(m_a * m_b);
        }

        public async Task<int> GetAxBError()
        {
            await Task.CompletedTask;
            throw new Exception("GetAxBError-Exception");
        }

        public Task LongMethod(int waitTime)
        {
            Thread.Sleep(waitTime);
            return Task.CompletedTask;
        }

        public async Task LongMethodWithError(int waitTime)
        {
            Thread.Sleep(waitTime);
            await Task.CompletedTask;
            throw new Exception("LongMethodWithError(" + waitTime + ")");
        }
    }
}
