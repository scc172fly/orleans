using Orleans.Serialization;

namespace UnitTests.GrainInterfaces
{
    /// <summary>
    /// 带有 <see cref="IOnDeserialized"/> 钩子的类型,用于测试内部序列器正确调用该类型。
    /// </summary>
    [Serializable]
    [Orleans.GenerateSerializer]
    [Orleans.SerializationCallbacks(typeof(Orleans.Runtime.OnDeserializedCallbacks))]
    public class TypeWithOnDeserializedHook : IOnDeserialized
    {
        [NonSerialized]
        public DeserializationContext? Context;

        [Orleans.Id(0)]
        public int Int { get; set; }

        void IOnDeserialized.OnDeserialized(DeserializationContext context)
        {
            this.Context = context;
        }
    }

    [Serializable]
    [Orleans.GenerateSerializer]
    public class BaseClassWithAutoProp
    {
        [Orleans.Id(0)]
        public int AutoProp { get; set; }
    }

    /// <summary>
    /// 代码生成测试,以确保被覆盖的、类型不同于
    /// 序列器生成过程中不使用基底自动prop
    /// </summary>
    [Serializable]
    [Orleans.GenerateSerializer]
    public class SubClassOverridingAutoProp : BaseClassWithAutoProp
    {
        public new string AutoProp { get => base.AutoProp.ToString(); set => base.AutoProp = int.Parse(value); }
    }
}
