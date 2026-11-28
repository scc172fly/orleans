namespace Tester.StorageFacet.Abstractions
{
    /// <summary>
    /// 主要存储功能接口。
    ///  这是用户实际需要的功能。
    /// </summary>
    /// <typeparam name="TState"></typeparam>
    public interface IExampleStorage<TState>
    {
        TState State { get; set; }

        Task Save();

        // 测试调用：用于验证分面（Facet）的连接是否正确
        string Name { get; }
        string GetExtendedInfo();
    }

    /// <summary>
    /// 应用层可以为该功能的每个实例（如果使用特性，则按 Grain 类型）
    ///  提供的功能配置信息。
    /// </summary>
    public interface IExampleStorageConfig
    {
        string? StateName { get; }
    }

    /// <summary>
    /// 功能配置实用工具类
    /// </summary>
    public class ExampleStorageConfig : IExampleStorageConfig
    {
        public ExampleStorageConfig(string stateName)
        {
            this.StateName = stateName;
        }

        public string StateName { get; }
    }

    /// <summary>
    /// 根据配置创建存储功能
    /// </summary>
    public interface IExampleStorageFactory
    {
        IExampleStorage<TState> Create<TState>(IExampleStorageConfig config);
    }

    /// <summary>
    /// 根据配置按名称创建存储功能
    /// </summary>
    public interface INamedExampleStorageFactory
    {
        IExampleStorage<TState> Create<TState>(string? name, IExampleStorageConfig config);
    }
}
