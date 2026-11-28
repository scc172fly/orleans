using System.Runtime.CompilerServices;
using EmptyFiles;

namespace Orleans.Durable.Journaling.Json.Tests;

internal static class VerifyModuleInitializer
{
    [ModuleInitializer]
    public static void Init()
    {
        // Verify 默认将未知扩展名视为二进制格式（并拒绝对二进制目标使用
        // 字符串重载）。此项目的快照文件使用 .jsonl 扩展名只是为了让编辑器
        // 将它们作为 JSON Lines 进行语法高亮；内容始终为 UTF-8 文本。
        // <see cref="FileExtensions"/> 注册表位于 <c>EmptyFiles</c> 包中，该包由 Verify
        // 传递引入。
        FileExtensions.AddTextExtension("jsonl");
    }
}
