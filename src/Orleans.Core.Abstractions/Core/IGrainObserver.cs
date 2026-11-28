using Orleans.Runtime;

namespace Orleans
{
    /// <summary>
    /// Grain 观察者的标记接口。
    /// 观察者用于接收 Grain 的通知，也就是说，它们代表
    /// 发布/订阅接口中的订阅方。
    /// </summary>
    public interface IGrainObserver : IAddressable
    {
    }
}
