namespace Orleans.Services
{
    /// <summary>
    /// Grain 服务客户端的基接口。
    /// </summary>
    /// <typeparam name="TGrainService">Grain 服务接口类型。</typeparam>
    public interface IGrainServiceClient<TGrainService>
        where TGrainService : IGrainService
    {
    }
}
