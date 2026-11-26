using Orleans.CodeGenerator.SyntaxGeneration;

namespace Orleans.CodeGenerator;

internal static class ApplicationPartAttributeGenerator
{
    /// <summary>返回每个命名组装的组装级应用程序部分属性。</summary>
    public static List<string> GenerateAttributes(string applicationPartAttribute, IEnumerable<string> applicationParts)
    {
        var attributes = new List<string>();
        foreach (var assemblyName in applicationParts)
        {
            attributes.Add($"[assembly: {applicationPartAttribute}({CodeText.QuotedText(assemblyName)})]");
        }

        return attributes;
    }
}
