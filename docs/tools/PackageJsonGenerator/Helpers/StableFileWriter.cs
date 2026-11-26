// 根据一项或多项协定获得 .NET 基金会许可证。
// 下 .NET 基金会根据麻省理工学院的许可证 将这个文件发给你

using System.Text;

namespace PackageJsonGenerator.Helpers;

internal static class StableFileWriter
{
    private static readonly UTF8Encoding s_utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    public static bool WriteIfChanged(string path, string content)
    {
        var normalizedContent = NormalizeLineEndings(content);

        if (File.Exists(path))
        {
            var existingContent = File.ReadAllText(path);
            if (string.Equals(existingContent, normalizedContent, StringComparison.Ordinal))
            {
                return false;
            }
        }

        File.WriteAllText(path, normalizedContent, s_utf8WithoutBom);
        return true;
    }

    private static string NormalizeLineEndings(string content)
    {
        return content.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
    }
}