using Mono.Cecil;
using Mono.Cecil.Cil;
using UnrealSharpWeaver.MetaData;
using UnrealSharpWeaver.Utilities;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace UnrealSharpWeaver.TypeProcessors;

public static class UnrealInterfaceProcessor
{ 
    public static void ProcessInterfaces(List<TypeDefinition> interfaces, ApiMetaData assemblyMetadata)
    {
        assemblyMetadata.InterfacesMetaData.Capacity = interfaces.Count;

        interfaces.Sort((a, b) =>
        {
            InterfaceMetaData aMetadata = new InterfaceMetaData(a);
            InterfaceMetaData bMetadata = new InterfaceMetaData(b);

            foreach (var Function in aMetadata.Functions)
            {
                if(Function.ReturnValue != null && Function.ReturnValue.PropertyDataType.CSharpType.FullName.Contains(bMetadata.TypeRef.FullName))
                {
                    return 1;
                }

                foreach (var Field in Function.Parameters)
                {
                    if (Field.PropertyDataType.CSharpType.FullName.Contains(bMetadata.TypeRef.FullName))
                    {
                        return 1;
                    }
                }
            }

            foreach (var Function in bMetadata.Functions)
            {

                if (Function.ReturnValue != null && Function.ReturnValue.PropertyDataType.CSharpType.FullName.Contains(aMetadata.TypeRef.FullName))
                {
                    return -1;
                }

                foreach (var Field in Function.Parameters)
                {
                    if (Field.PropertyDataType.CSharpType.FullName.Contains(aMetadata.TypeRef.FullName))
                    {
                        return -1;
                    }
                }
            }

            return 0;
        });

        for (var i = 0; i < interfaces.Count; ++i)
        {


            TypeDefinition interfaceType = interfaces[i];
            var a = new InterfaceMetaData(interfaceType);
            File.AppendAllText("log.txt", a.TypeRef.FullName + Environment.NewLine);

            assemblyMetadata.InterfacesMetaData.Add(a);
            
            CreateInterfaceMarshaller(interfaceType);
        }
    }

    public static void CreateInterfaceMarshaller(TypeDefinition interfaceType)
    {
        TypeDefinition structMarshallerClass = WeaverImporter.Instance.CurrentWeavingAssembly.CreateNewClass(interfaceType.Namespace, interfaceType.GetMarshallerClassName(), 
            TypeAttributes.Class | TypeAttributes.Public | TypeAttributes.BeforeFieldInit);
        
        FieldDefinition nativePointerField = structMarshallerClass.AddField("NativeInterfaceClassPtr", 
            WeaverImporter.Instance.IntPtrType, FieldAttributes.Public | FieldAttributes.Static);
        
        string interfaceName = interfaceType.GetEngineName();
        const bool finalizeMethod = true;
        
        ConstructorBuilder.CreateTypeInitializer(structMarshallerClass, Instruction.Create(OpCodes.Stsfld, nativePointerField), 
            [Instruction.Create(OpCodes.Call, WeaverImporter.Instance.GetNativeClassFromNameMethod)], interfaceName, finalizeMethod);
        
        MakeToNativeMethod(interfaceType, structMarshallerClass, nativePointerField);
        MakeFromNativeMethod(interfaceType, structMarshallerClass, nativePointerField);
    }
    
    public static void MakeToNativeMethod(TypeDefinition interfaceType, TypeDefinition structMarshallerClass, FieldDefinition nativePointerField)
    {
        MethodDefinition toNativeMarshallerMethod = interfaceType.AddMethod("ToNative", 
            WeaverImporter.Instance.VoidTypeRef,
            MethodAttributes.Public | MethodAttributes.Static, WeaverImporter.Instance.IntPtrType, WeaverImporter.Instance.Int32TypeRef, interfaceType);
        
        MethodReference toNativeMethod = WeaverImporter.Instance.ScriptInterfaceMarshaller.FindMethod("ToNative")!;
        toNativeMethod = FunctionProcessor.MakeMethodDeclaringTypeGeneric(toNativeMethod, interfaceType);
        
        ILProcessor toNativeMarshallerProcessor = toNativeMarshallerMethod.Body.GetILProcessor();
        toNativeMarshallerProcessor.Emit(OpCodes.Ldarg_0);
        toNativeMarshallerProcessor.Emit(OpCodes.Ldarg_1);
        toNativeMarshallerProcessor.Emit(OpCodes.Ldarg_2);
        toNativeMarshallerProcessor.Emit(OpCodes.Ldsfld, nativePointerField);
        toNativeMarshallerProcessor.Emit(OpCodes.Call, toNativeMethod);
        
        toNativeMarshallerMethod.FinalizeMethod();
    }
    
    public static void MakeFromNativeMethod(TypeDefinition interfaceType, TypeDefinition structMarshallerClass, FieldDefinition nativePointerField)
    {
        MethodDefinition fromNativeMarshallerMethod = structMarshallerClass.AddMethod("FromNative", 
            interfaceType,
            MethodAttributes.Public | MethodAttributes.Static,
            [WeaverImporter.Instance.IntPtrType, WeaverImporter.Instance.Int32TypeRef]);
        
        MethodReference fromNativeMethod = WeaverImporter.Instance.ScriptInterfaceMarshaller.FindMethod("FromNative")!;
        fromNativeMethod = FunctionProcessor.MakeMethodDeclaringTypeGeneric(fromNativeMethod, interfaceType);
        
        ILProcessor fromNativeMarshallerProcessor = fromNativeMarshallerMethod.Body.GetILProcessor();
        fromNativeMarshallerProcessor.Emit(OpCodes.Ldarg_0);
        fromNativeMarshallerProcessor.Emit(OpCodes.Ldarg_1);
        fromNativeMarshallerProcessor.Emit(OpCodes.Call, fromNativeMethod);
        fromNativeMarshallerProcessor.Emit(OpCodes.Ret);
        fromNativeMarshallerMethod.OptimizeMethod();
    }
}