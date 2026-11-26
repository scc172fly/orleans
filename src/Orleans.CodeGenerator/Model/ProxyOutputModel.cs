namespace Orleans.CodeGenerator.Model;

/// <summary>
/// 描述单个界面的代理输出,包括该文档拥有的可引用元数据名称。
/// </summary>
internal sealed record class ProxyOutputModel(
    ProxyInterfaceModel ProxyInterface,
    EquatableArray<string> OwnedInvokableMetadataNames,
    EquatableArray<string> EmittedInvokableMetadataNames,
    EquatableArray<string> OwnedInvokableActivatorMetadataNames,
    EquatableArray<CompoundTypeAliasModel> CompatibilityInvokableAliases,
    bool UseDeclaredInvokableFallback)
{
    public ProxyOutputModel(
        ProxyInterfaceModel proxyInterface,
        EquatableArray<string> ownedInvokableMetadataNames,
        bool useDeclaredInvokableFallback)
        : this(
            proxyInterface,
            ownedInvokableMetadataNames,
            ownedInvokableMetadataNames,
            EquatableArray<string>.Empty,
            EquatableArray<CompoundTypeAliasModel>.Empty,
            useDeclaredInvokableFallback)
    {
    }
}
