using Orleans.Serialization;

namespace UnitTests.GrainInterfaces
{
    /// <summary>
    /// 带有 <see cref="IOnDeserialized"/> 钩子的类型，用于测试内部序列化器是否正确调用此钩子。
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
    /// 代码生成测试，确保重写的自动实现属性若具有不同于
    /// 基类自动实现属性的类型，则不会在生成序列化器时被使用
    /// </summary>
    [Serializable]
    [Orleans.GenerateSerializer]
    public class SubClassOverridingAutoProp : BaseClassWithAutoProp
    {
        public new string AutoProp { get => base.AutoProp.ToString(); set => base.AutoProp = int.Parse(value); }
    }
}
