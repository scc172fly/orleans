namespace Tester.StorageFacet.Abstractions
{
    /// <summary>
    /// 初级存储功能接口。
    ///  这是用户需要的实际功能。
    /// </summary>
    /// <typeparam name="TState"></typeparam>
    public interface IExampleStorage<TState>
    {
        TState State { get; set; }

        Task Save();

        // 测试电话 -- -- 用于核查面线的安装工作
        string Name { get; }
        string GetExtendedInfo();
    }

    /// <summary>
    /// 应用层可向
    ///  字符的每个实例(使用属性时按 grain 类型) 。
    /// </summary>
    public interface IExampleStorageConfig
    {
        string? StateName { get; }
    }

    /// <summary>
    /// 功能配置通用类
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
    /// 根据配置创建存储功能,根据名称创建存储特性
    /// </summary>
    public interface INamedExampleStorageFactory
    {
        IExampleStorage<TState> Create<TState>(string? name, IExampleStorageConfig config);
    }
}
