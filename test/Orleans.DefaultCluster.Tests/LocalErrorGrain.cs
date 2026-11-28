namespace DefaultCluster.Tests
{
    /// <summary>
    /// 用于错误处理测试的测试 Grain 实现。
    /// 此 Grain 模拟各种错误情况，以测试异常传播和超时场景。
    /// 并非真实的 Orleans Grain，而是用于错误场景测试的本地模拟对象。
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
