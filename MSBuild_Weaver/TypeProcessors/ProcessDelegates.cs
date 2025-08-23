using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnrealSharpWeaver.MetaData;


public partial class WeaveFile
{
    public void ProcessDelegates(FileLogger2 fileLogger)
    {
        var classNode = root.DescendantNodes().OfType<ClassDeclarationSyntax>().First();

        foreach (var (ns, name, returnType, parameters, Attributes, DelegateDeclarationSyntax) in GetDelegates())
        {
            (new DelegateMetaData(DelegateDeclarationSyntax, ns)).Save();

            //new DelegateMetaData(new FunctionMetaData())
            fileLogger.LogAsync($"Processing delegate: {ns}.{name} -> {returnType}()");

            CreateDelegateMarshaller(ns, name, returnType, parameters);
            GenerateInitializeUnrealDelegate(ns, DelegateDeclarationSyntax);
            /*new DelegateMetaData(new FunctionMetaData()
            {
                Name = name,
                Namespace = ns,
                ReturnType = returnType,
                Parameters = parameters.Select(p =>
                {
                    var split = p.Split(' ');
                    return new ParameterMetaData()
                    {
                        Name = split[1],
                        Type = split[0]
                    };
                }).ToArray(),
                Attributes = Attributes
            });*/



            /*
            if(Attributes.Any(a => a.Contains("UMultiDelegate")))
            {
                CreateDelegateMarshaller(ns, name, returnType, parameters);
               // CreateMultipleDelegateConstructor(ns, name, returnType, parameters, classNode, );
                continue;
            } else if(Attributes.Any(a => a.Contains("USingleDelegate")))
            {
                CreateDelegateMarshaller(ns, name, returnType, parameters);
                //CreateSingleDelegateConstructor(ns, name, returnType, parameters);
            }*/
        }
    }


    public void CreateSingleDelegateConstructor(string ns, string delegateProperyName, string returnType, string[] parameters)
    {
        if(StaticConstructorLines.ContainsKey(delegateProperyName))
        {
            return;
        }
        StaticConstructorLines.Add(delegateProperyName, $@"
        nint num = FPropertyExporter.CallGetNativePropertyFromName(NativeClass, '{delegateProperyName}');
        MySingleDelegate_Offset = FPropertyExporter.CallGetPropertyOffset(num);
        U{returnType}.InitializeUnrealDelegate(num);
        ");
    }

    public void CreateMultipleDelegateConstructor(string ns, string delegateProperyName, string returnType, string[] parameters, ClassDeclarationSyntax Class)
    {
        if (StaticConstructorLines.ContainsKey(delegateProperyName))
        {
            return;
        }
        AddPropertiesToClass(Class,
            $@"private static int {delegateProperyName}_Offset = FPropertyExporter.CallGetPropertyOffset({delegateProperyName}_NativeProperty);",
            $@"private static readonly nint {delegateProperyName}_NativeProperty = FPropertyExporter.CallGetNativePropertyFromName(NativeClass, ""{delegateProperyName}"");"
            );

        StaticConstructorLines.Add(delegateProperyName, $@"
        U{returnType}.InitializeUnrealDelegate({delegateProperyName}_NativeProperty);
        ");
    }



    public IEnumerable<(string ns, string Name, string ReturnType, string[] Parameters, string[] Attributes, DelegateDeclarationSyntax decl)> GetDelegates()
    {
        return root
            .DescendantNodes()
            .OfType<DelegateDeclarationSyntax>()
            .Select(decl =>
                (
                    GetNamespaceDelegate(decl),
                    decl.Identifier.Text,
                    decl.ReturnType.ToString(),
                    decl.ParameterList.Parameters.Select(p => $"{p.Type} {p.Identifier.Text}").ToArray(),
                    decl.AttributeLists .SelectMany(al => al.Attributes) .Select(attr => attr.ToString()) .ToArray(),
                    decl
                ));
    }



    public void GenerateInitializeUnrealDelegate(string ns, DelegateDeclarationSyntax delegateDecl)
    {
        var delegateName = delegateDecl.Identifier.Text;
        var parameters = delegateDecl.ParameterList.Parameters;

        var sb = new StringBuilder();
        sb.Append(@$"
using System;
using UnrealSharp;
using UnrealSharp.Interop;
using UnrealSharp.Core.Marshallers;
namespace {ns} {{

    public class U{delegateName} : Delegate<{ns}.{delegateName}> {{

        public static U{delegateName} operator +(U{delegateName} thisDelegate, {delegateName} handler)
        {{
            thisDelegate.Add(handler);
            return thisDelegate;
        }}

        protected override {ns}.{delegateName} GetInvoker()
        {{
            return Invoker;
        }}

        public static U{delegateName} operator -(U{delegateName} thisDelegate, {delegateName} handler)
        {{
            thisDelegate.Remove(handler);
            return thisDelegate;
        }}

        public U{delegateName}() : base()
        {{
        }}

        public U{delegateName}(DelegateData data) : base(data)
        {{
        }}

        public U{delegateName}(UnrealSharp.CoreUObject.UObject targetObject, FName functionName) : base(targetObject, functionName)
        {{
        }}

        protected void Invoker(int a)
        {{
           ProcessDelegate(IntPtr.Zero);
        }}

");

        // Static fields
        sb.AppendLine($"\t\tpublic static nint SignatureFunction;");
        sb.AppendLine($"\t\tpublic static int FunctionParamSize;");

        foreach (var param in parameters)
        {
            var paramName = param.Identifier.Text;
            sb.AppendLine($"\t\tpublic static int {paramName}_Offset;");
            sb.AppendLine($"\t\tpublic static nint {paramName}_NativeProperty;");
        }

        sb.AppendLine();
        sb.AppendLine($"\t\tpublic static void InitializeUnrealDelegate(nint nativeProperty)");
        sb.AppendLine("\t\t{");
        sb.AppendLine($"\t\t\tSignatureFunction = FMulticastDelegatePropertyExporter.CallGetSignatureFunction(nativeProperty);");

        foreach (var param in parameters)
        {
            var paramName = param.Identifier.Text;
            sb.AppendLine($"\t\t\t{paramName}_Offset = FPropertyExporter.CallGetPropertyOffsetFromName(SignatureFunction, \"{paramName}\");");
            sb.AppendLine($"\t\t\t{paramName}_NativeProperty = FPropertyExporter.CallGetNativePropertyFromName(SignatureFunction, \"{paramName}\");");
        }

        sb.AppendLine($"\t\t\tFunctionParamSize = UFunctionExporter.CallGetNativeFunctionParamsSize(SignatureFunction);");
        sb.AppendLine("\t\t}\t\r\n}\t\r\n}");


        var fileName = Path.Combine(FullOutputDir, $"U{delegateName}.cs");

        File.WriteAllText(fileName, sb.ToString());

    }




    public void CreateDelegateMarshaller(string ns,string delegateTypeName, string returnType, string[] parameters)
    {
        if(File.Exists(Path.Combine(FullOutputDir, $"{delegateTypeName}Marshaller.cs")))
        {
            // Already exists, skip
            return;
        }

        var marshallerSource = $@"using System;
using UnrealSharp;
using UnrealSharp.Interop;
using UnrealSharp.Core.Marshallers;
using {ns};





public class U{delegateTypeName}Marshaller
{{
  public static U{delegateTypeName} FromNative(IntPtr obj0, int obj1)
  {{
    return new U{delegateTypeName}(BlittableMarshaller<DelegateData>.FromNative(obj0, 0));
  }}

  public static void ToNative(IntPtr buffer, int offset, DelegateData value)
    {{
        BlittableMarshaller<DelegateData>.ToNative(buffer, offset, value);
    }}
}}


namespace {ns}
{{

public static class U{delegateTypeName}Extensions
{{
     public static void Invoke(this TDelegate<{ns}.{delegateTypeName}> @delegate, {string.Join(", ", parameters)})
     {{
         @delegate.InnerDelegate.Invoke(a);
     }}
}}
}}
";

        // Write it out
        var fileName = Path.Combine(FullOutputDir, $"{delegateTypeName}Marshaller.cs");
        File.WriteAllText(fileName, marshallerSource);
    }



}

