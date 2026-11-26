using System;

namespace Orleans
{
    using Orleans.Runtime;

    /// <summary>
    /// Grain 的标记接口。
    /// </summary>
    public interface IGrain : IAddressable
    {
    }

    /// <summary>
    /// 使用 <see cref="Guid"/> 键的 Grain 的标记接口。
    /// </summary>
    public interface IGrainWithGuidKey : IGrain
    {
    }

    /// <summary>
    /// 使用 <see cref="long"/> 键的 Grain 的标记接口。
    /// </summary>
    public interface IGrainWithIntegerKey : IGrain
    {
    }

    /// <summary>
    /// 使用 <see cref="string"/> 键的 Grain 的标记接口。
    /// </summary>
    public interface IGrainWithStringKey : IGrain
    {
    }

    /// <summary>
    /// 使用复合键的 Grain 的标记接口。
    /// </summary>
    public interface IGrainWithGuidCompoundKey : IGrain
    {
    }

    /// <summary>
    /// 使用复合键的 Grain 的标记接口。
    /// </summary>
    public interface IGrainWithIntegerCompoundKey : IGrain
    {
    }
}
