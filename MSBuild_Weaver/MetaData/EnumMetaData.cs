

using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;

namespace UnrealSharpWeaver.MetaData
{
   public class EnumMetaData : TypeReferenceMetadata
   {

       public List<string> Items { get; set; }

       public EnumMetaData(EnumDeclarationSyntax enumMember) : base(enumMember, "UEnum")
       {
            // Get enum items

            Items = enumMember.Members.Select(m => m.Identifier.Text).ToList();
        }
    }
}