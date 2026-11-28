using Orleans.Runtime;

namespace Orleans
{
    /// <summary>
    /// 这是系统目标的标记接口。
    /// 系统目标是内部运行时对象，与 Grain 具有部分相同行为，但也存在一些限制，具体如下：
    /// 系统目标是可异步寻址的 Actor。
    /// 与 IGrain 一样，ISystemTarget 也会生成代理类。
    /// 系统目标由运行时调度器调度，遵循基于执行轮次的并发模型。
    /// </summary>
    public interface ISystemTarget : IAddressable
    {
    }
}
