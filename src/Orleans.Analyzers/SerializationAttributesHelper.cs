using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Orleans.Analyzers
{
    internal static class SerializationAttributesHelper
    {
        public static bool ShouldGenerateSerializer(INamedTypeSymbol symbol, INamedTypeSymbol generateSerializerAttributeSymbol)
        {
            if (!symbol.IsStatic && symbol.HasAttribute(generateSerializerAttributeSymbol))
            {
                return true;
            }

            return false;
        }

        public readonly record struct TypeAnalysis
        {
            public List<MemberDeclarationSyntax> UnannotatedMembers { get; init; }
            public uint NextAvailableId { get; init; }
        }

        public static TypeAnalysis AnalyzeTypeDeclaration(SemanticModel semanticModel, TypeDeclarationSyntax declaration,
            INamedTypeSymbol? idAttributeSymbol, INamedTypeSymbol? generateSerializerAttributeSymbol, INamedTypeSymbol? nonSerializedAttributeSymbol)
        {
            uint nextId = 0;
            var unannotatedSerializableMembers = new List<MemberDeclarationSyntax>();
            foreach (var member in declaration.Members)
            {
                // 跳过已有 [Id(x)] 特性的成员，但记录 x 的最大值，以便新添加的特性从该值之后开始编号。
                if (member.TryGetAttribute(semanticModel, idAttributeSymbol, out var attribute))
                {
                    var args = attribute.ArgumentList?.Arguments;
                    if (args.HasValue)
                    {
                        if (args.Value.Count > 0)
                        {
                            var idArg = args.Value[0];
                            if (idArg.Expression is LiteralExpressionSyntax literalExpression
                                && uint.TryParse(literalExpression.Token.ValueText, out var value)
                                && value >= nextId)
                            {
                                nextId = value + 1;
                            }
                        }
                    }

                    continue;
                }

                if (member is ConstructorDeclarationSyntax constructorDeclaration && constructorDeclaration.HasAttribute(semanticModel, generateSerializerAttributeSymbol))
                {
                    continue;
                }

                if (!member.IsInstanceMember() || !member.IsFieldOrAutoProperty() || member.HasAttribute(semanticModel, nonSerializedAttributeSymbol) || member.IsAbstract())
                {
                    // 无需添加任何特性。
                    continue;
                }

                unannotatedSerializableMembers.Add(member);
            }

            return new TypeAnalysis
            {
                UnannotatedMembers = unannotatedSerializableMembers,
                NextAvailableId = nextId,
            };
        }
    }
}
