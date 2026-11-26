using System;
using System.Threading.Tasks;

namespace Orleans.TestingHost.Utils
{
    /// <summary>
    /// 此类仅用于内部测试 。
    /// </summary>
    public class AsyncResultHandle
    {
        private bool done = false;
        private bool continueFlag = false;

        /// <summary> 重置当前结果处理 </summary>
        public virtual void Reset()
        {
            Exception = null;
            Result = null;
            done = false;
            continueFlag = false;
        }

        /// <summary> 获取或设置“安插”标志 </summary>
        public bool Done
        {
            get { return done; }
            set { done = value; }
        }

        /// <summary> 获取或设置续延标志 </summary>
        public bool Continue
        {
            get { return continueFlag; }
            set { continueFlag = value; }
        }

        /// <summary> 获取或设置结果控件的例外 </summary>
        public Exception? Exception { get; set; }

        /// <summary> 获取或设定结果处理器的值 </summary>
        public object? Result { get; set; }

        /// <summary>
        /// </summary>
        /// <param name="timeout"></param>
        /// <returns>返回 <c>true</c> 如果操作在超时前完成,</returns>
        public Task<bool> WaitForFinished(TimeSpan timeout)
        {
            return WaitFor(timeout, () => done);
        }

        /// <summary>
        /// </summary>
        /// <param name="timeout"></param>
        /// <returns>返回 <c>true</c> 如果操作在超时前完成,</returns>
        public Task<bool> WaitForContinue(TimeSpan timeout)
        {
            return WaitFor(timeout, () => continueFlag);
        }

        /// <summary>
        /// </summary>
        /// <param name="timeout"></param>
        /// <param name="checkFlag"></param>
        /// <returns>返回 <c>true</c> 如果操作在超时前完成,</returns>
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
