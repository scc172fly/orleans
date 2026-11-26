namespace Orleans.Services
{
    /// <summary>
    /// grain 服务客户端的基础接口。
    /// </summary>
    /// <typeparam name="TGrainService">下 grain 服务接口类型。</typeparam>
    public interface IGrainServiceClient<TGrainService>
        where TGrainService : IGrainService
    {
    }
}
