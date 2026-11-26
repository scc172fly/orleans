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
        // 散列代表运行在线性球池上;等待的简历在启动调度器上。
        var hash = await Task.Run(() => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(document)));
        await Archive(hash).WithId("archive");
        return hash;
    }
    // </orleans0209_violation>
#pragma warning restore ORLEANS0209

    // <orleans0209_fix_inline>
    public async DurableTask<string> SealInlineAsync(byte[] document)
    {
        // 效果记录散列, 并运行在激活的调度器上 。
        var hash = DurableTask.RunInline("hash", () => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(document)));
        await Archive(hash).WithId("archive");
        return hash;
    }
    // </orleans0209_fix_inline>

    // <orleans0209_fix_service>
    public async DurableTask<string> SealWithServiceAsync(byte[] document)
    {
        // 从事繁重的工作,例如无国籍工人 grain,该步骤等待。
        var hash = await DurableTask.Run(ct => _hasher.HashAsync(document, ct)).WithId("hash");
        await Archive(hash).WithId("archive");
        return hash;
    }
    // </orleans0209_fix_service>

    private async DurableTask<string> Archive(string hash) => _hash.Value = hash;
}
