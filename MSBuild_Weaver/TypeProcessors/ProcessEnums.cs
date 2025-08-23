using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;


public partial class WeaveFile
{
    public void ProcessEnums()
    {

        var enumNodes = root.DescendantNodes().OfType<EnumDeclarationSyntax>().ToList();
        foreach (var enumNode in enumNodes)
        {
            // check if has UEnum attribute
            var hasUEnumAttribute = enumNode.AttributeLists
                .SelectMany(a => a.Attributes)
                .Any(a => a.Name.ToString().Contains("UEnum"));
            if (!hasUEnumAttribute)
            {
                continue;
            }

            (new UnrealSharpWeaver.MetaData.EnumMetaData(enumNode)).Save();
        }

    }
}

