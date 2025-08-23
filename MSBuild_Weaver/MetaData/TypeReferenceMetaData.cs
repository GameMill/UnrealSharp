

using Microsoft.CodeAnalysis;

namespace UnrealSharpWeaver.MetaData
{
    
    public class TypeReferenceMetadata : BaseMetaData
    {
        // empty class used for hierarchy

        public TypeReferenceMetadata(SyntaxNode member, string attributeName = "") : base(member, attributeName)
        {
        }
    }
}