using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;


namespace UnrealSharpWeaver.MetaData
{
    public class BaseMetaData : IDisposable
    {
        public string Name { get; set; }
        public Dictionary<string, string> MetaData { get; set; }

        public BaseMetaData() { }

        // Non-serialized for JSON
        public readonly string AttributeName;
        [NonSerialized]
        public readonly SyntaxNode MemberNode;
        //public readonly CustomAttribute? BaseAttribute;
        // End non-serialized

        public BaseMetaData(SyntaxNode member, string attributeName = "")
        {
            MemberNode = member;
            AttributeName = attributeName;
            MetaData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            //BaseAttribute = MemberDefinition.CustomAttributes.FindAttributeByType(WeaverImporter.UnrealSharpAttributesNamespace, AttributeName)!;
            if (member is MemberDeclarationSyntax m)
            {
                AddMetaData(m); // Add any [UMetaData("key", "value")] attributes (general metadata attribute to allow support of any engine tag)
                AddMetaTagsNamespace(m); // Add all named attributes in the UnrealSharp.Attributes.MetaTags namespace
                AddBaseAttributes(m);    // Add fields from base attribute e.g. [UClass | UFunction | UEnum | UProperty | UStruct]
                AddDefaultCategory();   // Add Category="Default" if no category yet added
                AddBlueprintAccess();   // Add default Blueprint access if not already added
            }
        }

        public void Save()
        {
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(this, Newtonsoft.Json.Formatting.Indented);
            var dirPath = System.IO.Path.Combine(Environment.CurrentDirectory, "weave", "MetaData", this.GetType().Name);
            if (!Directory.Exists(dirPath))
            {
                Directory.CreateDirectory(dirPath);
            }
            System.IO.File.WriteAllText(System.IO.Path.Combine(dirPath, Name + ".json"), json);
        }

        protected void AddMetaData(MemberDeclarationSyntax member, SemanticModel semanticModel = null)
        {
            // Find all [UMetaData(...)] attributes on this member
            var metaDataAttributes = member.GetAttributes("UMetaData");

            foreach (var attr in metaDataAttributes)
            {
                // 1) Pull out the ArgumentList
                var argList = attr.ArgumentList;
                if (argList == null)
                    continue;

                // 2) Now Arguments is non-nullable
                var args = argList.Arguments;
                if (args.Count < 1)
                    continue;


                // Resolve the first argument as a string
                var key = args[0].Expression.ResolveStringArgument(semanticModel);

                // If only one argument was supplied, call the single-arg overload
                if (args.Count == 1)
                {
                    TryAddMetaData(key);
                }
                else
                {
                    // Otherwise resolve the second argument and call the two-arg overload
                    var value = args[1].Expression.ResolveStringArgument(semanticModel);
                    TryAddMetaData(key, value);
                }
            }
        }

        protected void AddMetaTagsNamespace(MemberDeclarationSyntax member, SemanticModel semanticModel = null)
        {
            // Find all [UMetaData(...)] attributes on this member
            IEnumerable<AttributeSyntax> metaDataAttributes = member.GetAttributesInNamespace("UnrealSharp.Attributes.MetaTags");

            foreach (AttributeSyntax attr in metaDataAttributes)
            {

                string key = attr.Name.ToFullString();
                if (key.EndsWith("Attribute"))
                {
                    key = key.Substring(0, -9); // Remove "Attribute" suffix
                }

                if (attr.ArgumentList == null)
                {
                    TryAddMetaData(key, "true");
                    continue; // No args
                }

                SeparatedSyntaxList<AttributeArgumentSyntax> args = attr.ArgumentList!.Arguments;
                if (args != null && args.Count > 0)
                {
                    var firstExpr = args[0].Expression;
                    var constVal = semanticModel.GetConstantValue(firstExpr);

                    // Use the semantic value if constant, otherwise fall back to literal text
                    var value = constVal.HasValue ? constVal.Value?.ToString() ?? "" : firstExpr.ExtractStringLiteral();

                    TryAddMetaData(key, value);
                }
                else
                {
                    // No ctor args → default to "true"
                    TryAddMetaData(key, "true");
                }
            }
        }


        public void AddBaseAttributes(MemberDeclarationSyntax member, SemanticModel semanticModel = null, string UnrealSharpNS = "UnrealSharp.Attributes.")   // e.g. "UnrealSharp.Attributes.YourAttribute"
        {
            // 1) Get the attribute data via the semantic model
            var symbol = semanticModel.GetDeclaredSymbol(member);
            if (symbol == null)
                return;

            var baseAttr = symbol.GetAttributes().FirstOrDefault(ad => ad.AttributeClass?.ToDisplayString() == UnrealSharpNS + AttributeName);

            if (baseAttr == null)
                return;

            // 2) Look for named arguments "DisplayName" and "Category"
            TryEmitNamed(baseAttr, "DisplayName");
            TryEmitNamed(baseAttr, "Category");

            // Local: pull one named arg and emit it
            void TryEmitNamed(AttributeData attr, string name)
            {
                if (!attr.NamedArguments.Any(kv => kv.Key == name))
                    return;

                var typedConst = attr.NamedArguments.First(kv => kv.Key == name).Value;
                var valText = typedConst.Value?.ToString() ?? string.Empty;

                TryAddMetaData(name, valText);
            }
        }

        public void TryAddMetaData(string key, string value = "")
        {
            MetaData[key] = value;
        }

        public void TryAddMetaData(string key, bool value)
        {
            TryAddMetaData(key, value ? "true" : "false");
        }

        public void TryAddMetaData(string key, int value)
        {
            TryAddMetaData(key, value.ToString());
        }

        public void TryAddMetaData(string key, ulong value)
        {
            TryAddMetaData(key, value.ToString());
        }

        public void TryAddMetaData(string key, float value)
        {
            TryAddMetaData(key, value.ToString());
        }

        public void TryAddMetaData(string key, double value)
        {
            TryAddMetaData(key, value.ToString());
        }

        public void TryAddMetaData(string key, object value)
        {
            TryAddMetaData(key, value?.ToString() ?? "");
        }

        public void AddDefaultCategory()
        {
            if (!MetaData.ContainsKey("Category"))
            {
                TryAddMetaData("Category", "Default");
            }
        }

        public void AddBlueprintAccess()
        {
            if (MetaData.ContainsKey("NotBlueprintType"))
            {
                return;
            }

            TryAddMetaData("BlueprintType", "true");
            TryAddMetaData("IsBlueprintBase", "true");
        }

        public void Dispose()
        {
        }
    }
}


public static class RoslynFlagHelpers
{
    /// <summary>
    /// Scans the syntax node’s AttributeLists for an attribute named <paramref name="attributeName"/> 
    /// (with or without “Attribute” suffix), and returns its first positional argument as a ulong.
    /// Returns 0 if the attribute is not present.
    /// </summary>
    public static ulong GetFlags(this MemberDeclarationSyntax member, string attributeName, SemanticModel? semanticModel = null)
    {
        // 1. Find the matching AttributeSyntax
        var attr = member.AttributeLists
            .SelectMany(al => al.Attributes)
            .FirstOrDefault(a => a.IsAttribute(attributeName));

        if (attr == null)
            return 0UL;

        // 2. Extract and parse the first argument
        var arg = attr.ArgumentList?.Arguments.FirstOrDefault();
        if (arg == null)
        {
            throw new InvalidOperationException(
                $"Attribute '{attributeName}' has no arguments.");
        }
            

        // 3a. If you have a semantic model, evaluate constants/enums
        if (semanticModel != null)
        {
            var cv = semanticModel.GetConstantValue(arg.Expression);
            if (cv.HasValue && cv.Value is IConvertible c)
                return Convert.ToUInt64(c);

            throw new InvalidOperationException(
                $"Cannot evaluate constant value for '{arg.Expression}'.");
        }

        // 3b. Syntax‐only fallback: parse literal text
        if (arg.Expression is LiteralExpressionSyntax lit && ulong.TryParse(lit.Token.ValueText, out var val))
        {
            return val;
        }

        // 3c. Strip suffixes (u/L) and retry
        var txt = arg.Expression.ToString().TrimEnd('u', 'U', 'l', 'L');
        if (ulong.TryParse(txt, out val))
        {
            return val;
        }

        throw new InvalidOperationException(
            $"Unable to parse '{arg.Expression}' as a ulong.");
    }

    // Helper to strip quotes off a literal expression
    public static string ExtractStringLiteral(this ExpressionSyntax expr)
    {
        if (expr is LiteralExpressionSyntax lit &&
            lit.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StringLiteralExpression))
        {
            return lit.Token.ValueText;
        }

        // Fallback: drop surrounding quotes if any
        return expr.ToString().Trim().Trim('"');
    }

    public static string ResolveStringArgument(this ExpressionSyntax expr, SemanticModel semanticModel)
    {
        // 1. SemanticModel route: handles const fields, enums, simple concatenation, etc.
        var constVal = semanticModel.GetConstantValue(expr);
        if (constVal.HasValue && constVal.Value is string s)
        {
            return s;
        }

        // 2. Syntax‐only route: when it’s a literal expression like "hello"
        if (expr is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.StringLiteralExpression))
        {
            return lit.Token.ValueText;
        }

        // 3. Fallback: strip quotes from the raw syntax text
        //    e.g. someone wrote @"foo" or included extra whitespace
        var txt = expr.ToString().Trim().Trim('"');
        return txt;
    }


    // Reuse your matching logic
    public static bool IsAttribute(this AttributeSyntax attr, string name)
    {
        var text = attr.Name.ToString();
        return text == name
            || text == name + "Attribute"
            || text.EndsWith("." + name)
            || text.EndsWith("." + name + "Attribute");
    }

    public static bool HasAttribute(this SyntaxNode node, string attributeName)
    {
        // Only member‐level nodes (types, methods, properties, fields, etc.) expose AttributeLists
        if (node is MemberDeclarationSyntax member)
        {
            return member.AttributeLists.SelectMany(al => al.Attributes).Any(attr => attr.IsAttribute(attributeName) );
        }

        return false;
    }

    public static IEnumerable<AttributeSyntax> GetAttributes(this SyntaxNode node, string attributeName)
    {
        // Only member‐level nodes (types, methods, properties, fields, etc.) expose AttributeLists
        if (node is MemberDeclarationSyntax member)
        {
            return member.AttributeLists.SelectMany(al => al.Attributes).Where(attr => attr.IsAttribute(attributeName));
        }

        return default;
    }

    public static IEnumerable<AttributeSyntax> GetAttributesInNamespace(this SyntaxNode node, string @namespace)
    {
        // Only member‐level nodes (types, methods, properties, fields, etc.) expose AttributeLists
        if (node is MemberDeclarationSyntax member)
        {
            return member.AttributeLists.SelectMany(al => al.Attributes)
                .Where(attr => attr.Name.ToString().StartsWith(@namespace + ".") || attr.Name.ToString() == @namespace);
        }
        return default;
    }
}
