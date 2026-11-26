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

    // 校验我们是否为 MyTypeWithAnInternalTypeField 生成自定义序列器, 因为它在组装中可见 。
    public interface IInternalReturnType : IGrainWithIntegerKey
    {
        Task<MyTypeWithAnInternalTypeField> Foo();
    }
}
