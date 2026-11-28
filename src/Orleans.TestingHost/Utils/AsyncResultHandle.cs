using System;
using System.Threading.Tasks;

namespace Orleans.TestingHost.Utils
{
    /// <summary>
    /// 此类仅供内部测试使用。
    /// </summary>
    public class AsyncResultHandle
    {
        private bool done = false;
        private bool continueFlag = false;

        /// <summary>重置当前结果句柄。</summary>
        public virtual void Reset()
        {
            Exception = null;
            Result = null;
            done = false;
            continueFlag = false;
        }

        /// <summary>获取或设置 Done 标志。</summary>
        public bool Done
        {
            get { return done; }
            set { done = value; }
        }

        /// <summary>获取或设置 Continue 标志。</summary>
        public bool Continue
        {
            get { return continueFlag; }
            set { continueFlag = value; }
        }

        /// <summary>获取或设置结果句柄的异常。</summary>
        public Exception? Exception { get; set; }

        /// <summary>获取或设置结果句柄的值。</summary>
        public object? Result { get; set; }

        /// <summary>
        /// </summary>
        /// <param name="timeout"></param>
        /// <returns>操作在超时前完成时返回 <c>true</c>。</returns>
        public Task<bool> WaitForFinished(TimeSpan timeout)
        {
            return WaitFor(timeout, () => done);
        }

        /// <summary>
        /// </summary>
        /// <param name="timeout"></param>
        /// <returns>操作在超时前完成时返回 <c>true</c>。</returns>
        public Task<bool> WaitForContinue(TimeSpan timeout)
        {
            return WaitFor(timeout, () => continueFlag);
        }

        /// <summary>
        /// </summary>
        /// <param name="timeout"></param>
        /// <param name="checkFlag"></param>
        /// <returns>操作在超时前完成时返回 <c>true</c>。</returns>
        public async Task<bool> WaitFor(TimeSpan timeout, Func<bool> checkFlag)
        {
            double remaining = timeout.TotalMilliseconds;
            while (!checkFlag())
            {
                if (remaining < 0)
                {
                    //throw new TimeoutException("Timeout waiting for result for " + timeout);
                    return false;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(200));
                remaining -= 200;
            }

            return true;
        }
    }
}
