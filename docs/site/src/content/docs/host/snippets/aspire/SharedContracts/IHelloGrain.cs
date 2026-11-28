namespace Orleans.Docs.Snippets.Aspire.SharedContracts;

// Aspire 集成示例所用的简单 Grain 接口。
public interface IHelloGrain : IGrainWithStringKey
{
    Task<string> SayHelloAsync(string name);
}
