using System;
using System.Diagnostics.CodeAnalysis;

namespace Orleans.Runtime
{
    /// <summary>
    /// 服务界面的接口,可以检测到健康状况。
    /// </summary>
    public interface IHealthCheckable
    {
        /// <summary>
        /// 返回表示此实例健康的值。
        /// </summary>
        /// <param name="lastCheckTime">最后一次检查此例健康。</param>
        /// <param name="reason">如果此方法返回 <see langword="false"/>, 这个参数将描述做出该裁决的原因。</param>
        /// <returns><see langword="true"/> 如果情况健康,则 <see langword="false"/> 。</returns>
        bool CheckHealth(DateTime lastCheckTime, [NotNullWhen(false)] out string? reason);
    }
}
