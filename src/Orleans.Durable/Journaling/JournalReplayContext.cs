namespace Orleans.Durable.Journaling;

/// <summary>
/// 提供日志状态在重放期间使用的服务。
/// </summary>
public readonly struct JournalReplayContext
{
    internal JournalReplayContext(JournaledStateManager manager)
    {
        ArgumentNullException.ThrowIfNull(manager);
        Manager = manager;
    }

    private JournaledStateManager Manager =>
        field ?? throw new InvalidOperationException("The journal replay context is not initialized.");

    /// <summary>
    /// 获取配置的写入日志格式键。
    /// </summary>
    public string WriteJournalFormatKey => Manager.WriteJournalFormatKey;

    /// <summary>
    /// 获取所属管理器用于解析日志服务的服务提供程序。
    /// </summary>
    /// <remarks>
    /// 对于激活实例拥有的管理器，这是激活实例的服务提供程序。
    /// 独立管理器使用宿主共享的日志服务。其调用方在构造状态机时提供任何
    /// 依赖作用域的状态机依赖项。
    /// </remarks>
    public IServiceProvider ServiceProvider => Manager.ServiceProvider;

    /// <summary>
    /// 解析 <paramref name="streamId"/> 对应的状态机。
    /// </summary>
    /// <param name="streamId">已持久化的日志流 ID。</param>
    /// <returns>该流对应的状态机。</returns>
    public IStateMachine ResolveStateMachine(JournalStreamId streamId) => Manager.ResolveStateMachine(streamId);

    /// <summary>
    /// 获取 <paramref name="entryFormatKey"/> 对应的命令编解码器。
    /// </summary>
    /// <typeparam name="TCodec">命令编解码器服务类型。</typeparam>
    /// <param name="entryFormatKey">正在重放的条目的日志格式键。</param>
    /// <param name="writeCommandCodec">配置的写入日志格式对应的命令编解码器。</param>
    /// <returns><paramref name="entryFormatKey"/> 对应的命令编解码器。</returns>
    public TCodec GetRequiredCommandCodec<TCodec>(string entryFormatKey, TCodec writeCommandCodec)
        where TCodec : notnull
    {
        ArgumentNullException.ThrowIfNull(writeCommandCodec);
        entryFormatKey = JournalFormatServices.ValidateJournalFormatKey(entryFormatKey);

        if (string.Equals(entryFormatKey, WriteJournalFormatKey, StringComparison.Ordinal))
        {
            return writeCommandCodec;
        }

        return JournalFormatServices.GetRequiredKeyedService<TCodec>(ServiceProvider, entryFormatKey);
    }
}
