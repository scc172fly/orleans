namespace UnitTests.GrainInterfaces
{
    // 注意：自管理的 Grain 只能实现一个 Grain 接口，因此必须复制粘贴代码，而不能通过派生子类实现

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
