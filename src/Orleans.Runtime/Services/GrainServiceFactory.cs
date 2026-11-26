using Orleans.Services;

namespace Orleans.Runtime
{
    /// <summary>
    /// 与 grain 服务互动的功能。
    /// </summary>
    public interface IGrainServiceFactory
    {
        /// <summary>
        /// 输入 grain 中输入的 grain 服务引用。
        /// 使用 grain 索引。
        /// </summary>
        /// <typeparam name="T">下 grain 服务接口。</typeparam>
        /// <param name="grainReference">下 grain 参考。</param>
        /// <returns>指定 grain 服务的参考。</returns>
        T CastToGrainServiceReference<T>(GrainReference grainReference) where T : IGrainService;
    }

    internal class GrainServiceFactory : IGrainServiceFactory
    {
        private readonly IRuntimeClient runtimeClient;

        public GrainServiceFactory(IRuntimeClient runtimeClient)
        {
            this.runtimeClient = runtimeClient;
        }

        public T CastToGrainServiceReference<T>(GrainReference grainReference) where T : IGrainService
            => this.runtimeClient.InternalGrainFactory.GetSystemTarget<T>(grainReference.GrainId);
    }
}
