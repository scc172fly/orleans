namespace Orleans.Docs.Snippets.Aspire.SharedContracts;

// 用于 Aspire 集成示例的简单 grain 界面
public interface IHelloGrain : IGrainWithStringKey
{
    Task<string> SayHelloAsync(string name);
}
