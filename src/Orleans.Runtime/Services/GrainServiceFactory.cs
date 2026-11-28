using Orleans.Services;

namespace Orleans.Runtime
{
    /// <summary>
    /// 提供与 Grain 服务交互的功能。
    /// </summary>
    public interface IGrainServiceFactory
    {
        /// <summary>
        /// 将 Grain 引用转换为带类型的 Grain 服务引用。
        /// 供 Grain 索引使用。
        /// </summary>
        /// <typeparam name="T">Grain 服务接口。</typeparam>
        /// <param name="grainReference">Grain 引用。</param>
        /// <returns>指定 Grain 服务的引用。</returns>
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
