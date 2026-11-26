using System.Runtime.CompilerServices;
using EmptyFiles;

namespace Orleans.Durable.Journaling.Json.Tests;

internal static class VerifyModuleInitializer
{
    [ModuleInitializer]
    public static void Init()
    {
        // 校验默认值, 将未知的扩展名作为二进制处理( 并拒绝字符串超载)
        // 双进目标))。在此工程中, 抓图文件使用.jsonl 扩展名, 纯粹是编辑
        // 语法高亮显示为 JSON 行; 内容总是 UTF-8 文本。 The the type
        // <see cref="FileExtensions"/> 登记在 <c>EmptyFiles</c> 套件包已带入
        // 由核查中转。
        FileExtensions.AddTextExtension("jsonl");
    }
}
