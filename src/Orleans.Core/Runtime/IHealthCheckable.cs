using System;
using System.Diagnostics.CodeAnalysis;

namespace Orleans.Runtime
{
    /// <summary>
    /// 可探测健康状态的服务接口。
    /// </summary>
    public interface IHealthCheckable
    {
        /// <summary>
        /// 返回指示此实例健康状态的值。
        /// </summary>
        /// <param name="lastCheckTime">上一次检查此实例健康状态的时间。</param>
        /// <param name="reason">如果此方法返回 <see langword="false"/>，则此参数描述作出该判断的原因。</param>
        /// <returns><see langword="true"/> 表示实例健康，否则为 <see langword="false"/>。</returns>
        bool CheckHealth(DateTime lastCheckTime, [NotNullWhen(false)] out string? reason);
    }
}
