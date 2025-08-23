using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;


public partial class WeaveFile
{
    public void AddRequiredNamespaces()
    {
        var usings = new List<string>
        {
            "System",
            "UnrealSharp",
            "UnrealSharp.Attributes",
            "UnrealSharp.Core.Attributes",
            "UnrealSharp.CoreUObject",
            "UnrealSharp.Engine",
            "UnrealSharp.Interop",
            "UnrealSharp.Core.Marshallers"
        };

        // Cast root to CompilationUnitSyntax
        var compilationUnit = (CompilationUnitSyntax)root;

        // Get existing using names
        var existingUsings = compilationUnit.Usings
            .Select(u => u.Name.ToString())
            .ToArray();

        // Filter out duplicates
        var newUsings = usings
            .Where(u => !existingUsings.Contains(u))
            .Select(u => SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(u)))
            .ToArray();

        // Add new usings to the compilation unit
        root = compilationUnit.AddUsings(newUsings);
    }



    // Currently Handeles properties and delegates, Should move to separate files later.
    // TODO: Handle methods and rewrite UE methods (BeginPlay into BeginPlay_Implementation, etc)
    public void ProcessClasses(FileLogger2 fileLogger)
    {
        AddRequiredNamespaces();
        var classesNodes = root.DescendantNodes().OfType<ClassDeclarationSyntax>().ToList();
        foreach (var classNode in classesNodes)
        {
            string className = classNode.Identifier.Text;
            string classNamespace = GetNamespaceClass(classNode);
            string assemblyName = GetAssemblyName(classNode);
            AddPropertiesToClass(classNode, $@"private static nint NativeClass = UCoreUObjectExporter.CallGetNativeClassFromName(""{assemblyName}"", ""{classNamespace}"", ""{className}"");");

            var ClassMetaData = new UnrealSharpWeaver.MetaData.ClassMetaData(classNode);

            StaticConstructorLines.Clear();
            foreach (var PropertyNode in GetProperties(classNode))
            {
                var propertyTypeFull = GetPropertyTypeFull(PropertyNode);
                var propertyName = PropertyNode.Identifier.Text;
                if (propertyTypeFull.Contains("TMulticastDelegate"))
                {
                    CreateMultipleDelegateConstructor(GetNamespaceClass(classNode), propertyName, GetPropertyTType(PropertyNode).FirstOrDefault(), default, classNode);
                }
                else if (propertyTypeFull.Contains("TSingleDelegate"))
                {
                    CreateSingleDelegateConstructor(GetNamespaceClass(classNode), propertyName, GetPropertyTType(PropertyNode).FirstOrDefault(), default);
                } else
                {
                    UpdatePropertyGetterSetter(PropertyNode, classNode);
                }
            }
            ClassMetaData.Save();

        }
    }


    public void AddStaticConstructor(ClassDeclarationSyntax classNode)
    {
        // Create a static constructor if it doesn't exist
        if (!classNode.Members.Any(m => m is ConstructorDeclarationSyntax ctor && ctor.Modifiers.Any(SyntaxKind.StaticKeyword)))
        {
            // Create a static constructor with body of StaticConstructorLines
            var staticConstructor = SyntaxFactory.ConstructorDeclaration(classNode.Identifier.Text)
                .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.StaticKeyword)))
                .WithBody(SyntaxFactory.Block(
                    StaticConstructorLines.Select(line => SyntaxFactory.ParseStatement(line.Value))
                ));
            var newClassNode = classNode.AddMembers(staticConstructor);
            root = root.ReplaceNode(classNode, newClassNode);
        }

    }
    public string GetPropertyTypeFull(PropertyDeclarationSyntax propertyNode)
    {
        // Get the type of the property
        var type = propertyNode.Type.ToString();
        // Handle generic types
        if (propertyNode.Type is GenericNameSyntax genericName)
        {
            type = $"{genericName.Identifier.Text}<{string.Join(", ", genericName.TypeArgumentList.Arguments.Select(a => a.ToString()))}>";
        }
        return type;
    }
    public IEnumerable<string> GetPropertyTType(PropertyDeclarationSyntax propertyNode)
    {
        // Handle generic types
        if (propertyNode.Type is GenericNameSyntax genericName)
        {
            return genericName.TypeArgumentList.Arguments.Select(a => a.ToString());
        }
        return null;
    }



    public List<PropertyDeclarationSyntax> GetProperties(ClassDeclarationSyntax classNode)
    {
        return classNode.Members
            .OfType<PropertyDeclarationSyntax>()
            .ToList();
    }

    public void UpdatePropertyGetterSetter(PropertyDeclarationSyntax propertyNode, ClassDeclarationSyntax classNode, SemanticModel semanticModel = null)
    {
        WeaveFile.logger.LogAsync($"Processing property {propertyNode.Identifier.Text}");
        string typeName = propertyNode.Type.ToString();

        //if (!IsUProperty(propertyNode, semanticModel))
        //{
           // WeaveFile.logger.LogAsync($"Skipping property {propertyNode.Identifier.Text} of type {typeName} as it is not marked with [UProperty(DefaultComponent = true)]");
            // Possilby switch to UClass Check and UProperty check instead of namespace check.
           //return; // Skip processing if not in UnrealSharp namespace.
        //}


        var accessorList = propertyNode.AccessorList;
        var propertyName = propertyNode.Identifier.Text;
        var propertyType = propertyNode.Type.ToString();

        string getBodyCode;
        string setBodyCode;

        if (propertyType.StartsWith("U"))
        {
            getBodyCode = @$"
return DefaultComponentMarshaller<{propertyType}>.FromNative(
    this, 
    ""{propertyName}"", 
    IntPtr.Add(NativeObject, {propertyName}_Offset), 
    0
);";
            setBodyCode = @$"
DefaultComponentMarshaller<{propertyType}>.ToNative(
    IntPtr.Add(NativeObject, {propertyName}_Offset), 
    0, 
    value
);";
        }
        else // for now assume all non-U are primitive types
        {
            getBodyCode = @$"
return BlittableMarshaller<{propertyType}>.FromNative(
    IntPtr.Add(NativeObject, {propertyName}_Offset), 
    0
);";
            setBodyCode = @$"
BlittableMarshaller<{propertyType}>.ToNative(
    IntPtr.Add(NativeObject, {propertyName}_Offset), 
    0, 
    value
);";

        }
        AddPropertiesToClass(classNode, @$"private static int {propertyName}_Offset;");
        StaticConstructorLines.Add(propertyName+"GetterSetter", $@"
{propertyName}_Offset = FPropertyExporter.CallGetPropertyOffset(FPropertyExporter.CallGetNativePropertyFromName(NativeClass, ""{propertyName}""));"); // TODO: Add back nint var with incrementer for CallGetNativePropertyFromName return value

        ReplacePropertyGetterSetterNode(propertyNode, getBodyCode, setBodyCode);
    }

    public bool IsUProperty(PropertyDeclarationSyntax propertyNode, SemanticModel semanticModel = null)
    {

        return propertyNode.AttributeLists
                .SelectMany(al => al.Attributes)
                .Any(attr => attr.Name.ToString().Contains("UProperty"));



    }


    public void ReplacePropertyGetterSetterNode(PropertyDeclarationSyntax propertyNode, string getBody, string setBody)
    {

        logger.LogAsync($"Replacing property {propertyNode.Identifier.Text} getter and setter with generated code.");
        if (!root.DescendantNodes().Contains(propertyNode))
        {
            var originalNode = root.DescendantNodes().OfType<PropertyDeclarationSyntax>().FirstOrDefault(p => p.Identifier.Text == propertyNode.Identifier.Text);
            propertyNode = originalNode;
        }

        if (!root.DescendantNodes().Contains(propertyNode))
        {
            WeaveFile.logger.LogAsync($"Failed to find property {propertyNode.Identifier.Text} in the syntax tree.");
        }


            var newProperty = propertyNode.WithAccessorList(
        SyntaxFactory.AccessorList(
            SyntaxFactory.List(new[]
            {
                SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                    .WithBody(SyntaxFactory.Block(
                        SyntaxFactory.SingletonList(
                            SyntaxFactory.ParseStatement(getBody)
                        )
                    )),
                SyntaxFactory.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                    .WithBody(SyntaxFactory.Block(
                        SyntaxFactory.SingletonList(
                            SyntaxFactory.ParseStatement(setBody)
                        )
                    ))
            })
        )).WithModifiers(propertyNode.Modifiers);
        root = root.ReplaceNode(propertyNode, newProperty);

    }
}
