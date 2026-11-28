namespace UnitTests.GrainInterfaces
{
    public interface ILivenessTestGrain : IGrainWithIntegerKey
    {
        // 可以独立设置的标签
        Task<string> GetLabel();

        Task SetLabel(string label);

        Task<string> GetRuntimeInstanceId();

        Task<string> GetUniqueId();

        Task<ILivenessTestGrain> GetGrainReference();

        Task StartTimer();

    }
}
