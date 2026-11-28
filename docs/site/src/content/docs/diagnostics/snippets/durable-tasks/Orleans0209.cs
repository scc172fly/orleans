namespace Documentation.Diagnostics.DurableTasks;

public sealed class DocumentGrain : DurableGrain
{
    private readonly IDocumentHasher _hasher;
    private readonly IDurableValue<string?> _hash;

    public DocumentGrain(IDocumentHasher hasher)
    {
        _hasher = hasher;
        _hash = StateManager.GetOrAddValue<string?>("hash");
    }

#pragma warning disable ORLEANS0209
    // <orleans0209_violation>
    public async DurableTask<string> SealAsync(byte[] document)
    {
        // 哈希委托在线程池上执行；await 之后在激活实例的调度器上恢复执行。
        var hash = await Task.Run(() => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(document)));
        await Archive(hash).WithId("archive");
        return hash;
    }
    // </orleans0209_violation>
#pragma warning restore ORLEANS0209

    // <orleans0209_fix_inline>
    public async DurableTask<string> SealInlineAsync(byte[] document)
    {
        // 效果记录哈希，并在激活实例的调度器上运行。
        var hash = DurableTask.RunInline("hash", () => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(document)));
        await Archive(hash).WithId("archive");
        return hash;
    }
    // </orleans0209_fix_inline>

    // <orleans0209_fix_service>
    public async DurableTask<string> SealWithServiceAsync(byte[] document)
    {
        // 耗时工作在服务（例如无状态工作者 Grain）中运行，由步骤等待该服务。
        var hash = await DurableTask.Run(ct => _hasher.HashAsync(document, ct)).WithId("hash");
        await Archive(hash).WithId("archive");
        return hash;
    }
    // </orleans0209_fix_service>

    private async DurableTask<string> Archive(string hash) => _hash.Value = hash;
}
