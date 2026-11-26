namespace Orleans.Durable.Journaling;

/// <summary>
/// 提供期刊刊出的国家在重播时使用的服务。
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
    /// 获取配置的写日记格式密钥。
    /// </summary>
    public string WriteJournalFormatKey => Manager.WriteJournalFormatKey;

    /// <summary>
    /// 获得拥有权经理用来解决日记服务问题的服务提供者。
    /// </summary>
    /// <remarks>
    /// 对于启动所有者来说,这是激活的服务提供者。
    /// 独立管理人员使用主机共享日记服务。
    /// 国家机器建造时依赖范围的国家机器依赖。
    /// </remarks>
    public IServiceProvider ServiceProvider => Manager.ServiceProvider;

    /// <summary>
    /// 确定 <paramref name="streamId"/>的国家机器。
    /// </summary>
    /// <param name="streamId">持续日记流代号 。</param>
    /// <returns>流的状态机器。</returns>
    public IStateMachine ResolveStateMachine(JournalStreamId streamId) => Manager.ResolveStateMachine(streamId);

    /// <summary>
    /// 获取 <paramref name="entryFormatKey"/>的命令代码码。
    /// </summary>
    /// <typeparam name="TCodec">命令编码服务类型。</typeparam>
    /// <param name="entryFormatKey">重播条目的日记格式密钥。</param>
    /// <param name="writeCommandCodec">配置的写日记日记格式的命令编码。</param>
    /// <returns> <paramref name="entryFormatKey"/>的指令编码器。</returns>
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
