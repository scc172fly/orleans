using Orleans.Runtime;

namespace Orleans
{
    /// <summary>
    /// 这是系统目标的标记界面 。
    /// 系统目标为内部运行时间对象,与 grains具有某种行为共通性,但也施加某些限制。特别是:
    /// 系统目标是可与非同步的行为者打交道。
    /// 正在为 ISystemTarget生成代理类, 就像IGrain一样
    /// 系统目标由运行时间定时器安排,并遵循以回合为基础的货币计算。
    /// </summary>
    public interface ISystemTarget : IAddressable
    {
    }
}
