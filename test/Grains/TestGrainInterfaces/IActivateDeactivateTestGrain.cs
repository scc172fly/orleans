namespace UnitTests.GrainInterfaces
{
    // 注:自管理只能安装一个 grain 接口,所以必须使用复制纸而不是子分类 

    public interface ISimpleActivateDeactivateTestGrain : IGrainWithIntegerKey
    {
        Task<string> DoSomething();
        Task DoDeactivate();
    }

    public interface ITailCallActivateDeactivateTestGrain : IGrainWithIntegerKey
    {
        Task<string> DoSomething();
        Task DoDeactivate();
    }

    public interface ILongRunningActivateDeactivateTestGrain : IGrainWithIntegerKey
    {
        Task<string> DoSomething();
        Task DoDeactivate();
    }

    public interface IBadActivateDeactivateTestGrain : IGrainWithIntegerKey
    {
        Task ThrowSomething();
        Task<long> GetKey();
    }

    public interface IBadConstructorTestGrain : IGrainWithIntegerKey
    {
        Task<string> DoSomething();
    }

    public interface ITaskActionActivateDeactivateTestGrain : IGrainWithIntegerKey
    {
        Task<string> DoSomething();
        Task DoDeactivate();
    }

    public interface ICreateGrainReferenceTestGrain : IGrainWithIntegerKey
    {
        Task<string> DoSomething();

        Task ForwardCall(IBadActivateDeactivateTestGrain otherGrain);
    }

    public interface IDeactivatingWhileActivatingTestGrain : IGrainWithIntegerKey
    {
        Task<string> DoSomething();
    }

}
