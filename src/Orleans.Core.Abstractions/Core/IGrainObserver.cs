using Orleans.Runtime;

namespace Orleans
{
    /// <summary>
    /// 为 grain 观察员设置的标记界面。
    /// 观察员利用观察员从以下组织收到通知: grains;即,它们代表着 
    /// 出版商/订户界面。
    /// </summary>
    public interface IGrainObserver : IAddressable
    {
    }
}
