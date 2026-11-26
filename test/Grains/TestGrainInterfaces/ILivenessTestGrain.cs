namespace UnitTests.GrainInterfaces
{
    public interface ILivenessTestGrain : IGrainWithIntegerKey
    {
        // 能够设置的单独标签
        Task<string> GetLabel();

        Task SetLabel(string label);

        Task<string> GetRuntimeInstanceId();

        Task<string> GetUniqueId();

        Task<ILivenessTestGrain> GetGrainReference();

        Task StartTimer();

    }
}
