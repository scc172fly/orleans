using System.IO;
using System.Xml.Linq;
using Orleans;
using Orleans.Serialization.Cloning;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.Serializers;

namespace TestGrainInterfaces;

/// <summary>
/// 用于测试的XML文件序列和深底复制。
/// </summary>
[RegisterSerializer]
[RegisterCopier]
public class XDocumentSerialization : GeneralizedReferenceTypeSurrogateCodec<XDocument, XDocumentSurrogate>, IDeepCopier<XDocument>
{
    public XDocumentSerialization(IValueSerializer<XDocumentSurrogate> surrogateSerializer) : base(surrogateSerializer)
    {
    }

    public override XDocument ConvertFromSurrogate(ref XDocumentSurrogate surrogate) => XDocument.Load(new StringReader(surrogate.Value));
    public override void ConvertToSurrogate(XDocument value, ref XDocumentSurrogate surrogate) => surrogate.Value = value.ToString();
    [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(input))]
    public XDocument? DeepCopy(XDocument? input, CopyContext context) => input is null ? null : new(input);
}

[GenerateSerializer]
public struct XDocumentSurrogate
{
    [Id(0)]
    public string Value { get; set; }
}
