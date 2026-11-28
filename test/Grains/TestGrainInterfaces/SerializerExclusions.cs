namespace Orleans.UnitTest.GrainInterfaces
{
    [Serializable]
    [GenerateSerializer]
    public class MyTypeWithAnInternalTypeField
    {
        [Id(0)]
        private readonly MyInternalDependency _dependency;

        public MyTypeWithAnInternalTypeField()
        {
            _dependency = new MyInternalDependency();
        }

        [GenerateSerializer]
        internal class MyInternalDependency
        {
        }
    }

    // 验证我们确实为 MyTypeWithAnInternalTypeField 生成自定义序列化器，因为该类型在程序集内可见。
    public interface IInternalReturnType : IGrainWithIntegerKey
    {
        Task<MyTypeWithAnInternalTypeField> Foo();
    }
}
