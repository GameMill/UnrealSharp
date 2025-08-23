

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using UnrealSharp.Attributes;

namespace UnrealSharpWeaver.MetaData
{
   
    public class ClassMetaData : TypeReferenceMetadata
    {
        public string ParentClass { get; set; }
        public List<PropertyMetaData> Properties { get; set; }
        public List<FunctionMetaData> Functions { get; set; }
        public List<FunctionMetaData> VirtualFunctions { get; set; }
        public List<TypeReferenceMetadata> Interfaces { get; set; }
        public string ConfigCategory { get; set; } 
        public ClassFlags ClassFlags { get; set; }


        public ClassMetaData(ClassDeclarationSyntax member) : base(member, "UClass")
        {
            ParentClass = GetBaseClassName(member); // TODO: This was a direct reference to the base class TypeReferenceMetadata, Check if we need more info here
            Properties = new List<PropertyMetaData>();
            Functions = new List<FunctionMetaData>();
            VirtualFunctions = new List<FunctionMetaData>(); 
            Interfaces = new List<TypeReferenceMetadata>(); // TODO: This was a direct reference to the interface TypeReferenceMetadata, Check if we need more info here
            ConfigCategory = GetClassCategoryFromAttributes(member); // UClass(Category = "")
            ClassFlags = GetClassFlagsFromAttributes(member); // Get the UClass flags from attributes

        }

        string? GetBaseClassName(ClassDeclarationSyntax classDeclaration)
        {
            var baseTypes = classDeclaration.BaseList?.Types;
            if (!baseTypes.HasValue || baseTypes.Value.Count == 0)
                return null;

            // First type is usually the base class (interfaces follow)
            var baseTypeSyntax = baseTypes.Value[0];
            return baseTypeSyntax.Type.ToString(); // e.g. "AActor"
        }

        public ClassFlags GetClassFlagsFromAttributes(ClassDeclarationSyntax Member)
        {
            var classFlags = ClassFlags.None;
            var attributeLists = Member.AttributeLists;
            foreach (var attributeList in attributeLists)
            {
                foreach (var attribute in attributeList.Attributes)
                {
                    var attributeName = attribute.Name.ToString();
                    if (attributeName == nameof(UClassAttribute) || attributeName == "UClass")
                    {
                        if (attribute.ArgumentList != null)
                        {
                            foreach (var argument in attribute.ArgumentList.Arguments)
                            {
                                var expr = argument.Expression.ToString();
                                if (System.Enum.TryParse<ClassFlags>(expr, out var flag))
                                {
                                    classFlags |= flag;
                                }
                            }
                        }
                    }
                }
            }
            return classFlags;
        }

        public string GetClassCategoryFromAttributes(ClassDeclarationSyntax Member)
        {
            var configCategory = string.Empty;
            var attributeLists = Member.AttributeLists;
            foreach (var attributeList in attributeLists)
            {
                foreach (var attribute in attributeList.Attributes)
                {
                    var attributeName = attribute.Name.ToString();
                    if (attributeName == nameof(UClassAttribute) || attributeName == "UClass")
                    {
                        if (attribute.ArgumentList != null)
                        {
                            foreach (var argument in attribute.ArgumentList.Arguments)
                            {
                                var expr = argument.Expression.ToString();
                                // Assuming the category is passed as a named argument like Category = "MyCategory"
                                if (argument.NameEquals != null && argument.NameEquals.Name.Identifier.Text == "Category")
                                {
                                    configCategory = expr.Trim('"'); // Remove quotes
                                }
                            }
                        }
                    }
                }
            }
            return configCategory;
        }

        public void CreateFunctionList(ClassDeclarationSyntax Member)
        {
            foreach (var member in Member.Members)
            {
                if (member is MethodDeclarationSyntax methodDeclaration)
                {
                    FunctionMetaData functionMetaData = new FunctionMetaData(methodDeclaration);

                    if (functionMetaData.IsAsyncUFunction(methodDeclaration))
                    {
                        functionMetaData.RewriteMethodAsAsyncUFunctionImplementation(methodDeclaration);
                        continue;
                    }
                    bool isBlueprintOverride = functionMetaData.IsBlueprintEventOverride(methodDeclaration);
                    bool isInterfaceFunction = functionMetaData.IsInterfaceFunction(methodDeclaration);
                    if (functionMetaData.IsUFunction(methodDeclaration) && !isInterfaceFunction)
                    {
                        if (isBlueprintOverride)
                        {
                            throw new System.Exception($"{methodDeclaration.Identifier.Text} is a Blueprint override and cannot be marked as a UFunction again.");
                        }
                        Functions.Add(functionMetaData);
                    }
                    if (isBlueprintOverride || isInterfaceFunction)
                    {
                        EFunctionFlags functionFlags = EFunctionFlags.None;
                        if (isInterfaceFunction)
                        {
                            // MethodDefinition interfaceFunction = FunctionMetaData.TryGetInterfaceFunction(method)!;
                            // functionFlags = interfaceFunction.GetFunctionFlags();
                        }
                        VirtualFunctions.Add(functionMetaData);
                    }
                }
            }
        }
        /*
           // Non-serialized for JSON
           public bool HasProperties => Properties.Count > 0;
           private readonly TypeDefinition _classDefinition;
           // End non-serialized

           public ClassMetaData(TypeDefinition type) : base(type, TypeDefinitionUtilities.UClassAttribute)
           {
               _classDefinition = type;

               Properties = [];
               Functions = [];
               VirtualFunctions = [];

               ConfigCategory = string.Empty;
               Interfaces = [];

               PopulateInterfaces();
               PopulateProperties();
               PopulateFunctions();

               AddConfigCategory();

               ParentClass = new TypeReferenceMetadata(type.BaseType.Resolve());
               ClassFlags |= GetClassFlags(type, AttributeName) | ClassFlags.CompiledFromBlueprint;

               // Force DefaultConfig if Config is set and no other config flag is set
               if (ClassFlags.HasFlag(ClassFlags.Config) &&
                   !ClassFlags.HasFlag(ClassFlags.GlobalUserConfig | ClassFlags.DefaultConfig | ClassFlags.ProjectUserConfig))
               {
                   ClassFlags |= ClassFlags.DefaultConfig;
               }

               if (type.IsChildOf(WeaverImporter.Instance.UActorComponentDefinition))
               {
                   TryAddMetaData("BlueprintSpawnableComponent", true);
               }
           }

           private void AddConfigCategory()
           {
               CustomAttribute uClassAttribute = _classDefinition.GetUClass()!;
               CustomAttributeArgument? configCategoryProperty = uClassAttribute.FindAttributeField(nameof(ConfigCategory));
               if (configCategoryProperty != null)
               {
                   ConfigCategory = (string) configCategoryProperty.Value.Value;
               }
           }

           private void PopulateProperties()
           {
               if (_classDefinition.Properties.Count == 0)
               {
                   return;
               }

               Properties = [];

               foreach (PropertyDefinition property in _classDefinition.Properties)
               {
                   CustomAttribute? uPropertyAttribute = property.GetUProperty();

                   if (uPropertyAttribute == null)
                   {
                       continue;
                   }

                   PropertyMetaData propertyMetaData = new PropertyMetaData(property);
                   Properties.Add(propertyMetaData);

                   if (propertyMetaData.IsInstancedReference)
                   {
                       ClassFlags |= ClassFlags.HasInstancedReference;
                   }
               }
           }

           void PopulateFunctions()
           {
               if (_classDefinition.Methods.Count == 0)
               {
                   return;
               }

               Functions = [];
               VirtualFunctions = [];

               for (var i = _classDefinition.Methods.Count - 1; i >= 0; i--)
               {
                   MethodDefinition method = _classDefinition.Methods[i];

                   if (method.HasParameters)
                   {
                       var paramNameSet = new HashSet<string>();
                       var uniqueNum = 0;
                       foreach (var param in method.Parameters)
                       {
                           if (!paramNameSet.Add(param.Name))
                           {
                               param.Name = $"{param.Name}_{uniqueNum++}";
                           }
                       }
                   }

                   if (FunctionMetaData.IsAsyncUFunction(method))
                   {
                       FunctionProcessor.RewriteMethodAsAsyncUFunctionImplementation(method);
                       continue;
                   }

                   bool isBlueprintOverride = FunctionMetaData.IsBlueprintEventOverride(method);
                   bool isInterfaceFunction = FunctionMetaData.IsInterfaceFunction(method);

                   if (method.IsUFunction() && !isInterfaceFunction)
                   {
                       if (isBlueprintOverride)
                       {
                           throw new Exception($"{method.FullName} is a Blueprint override and cannot be marked as a UFunction again.");
                       }

                       FunctionMetaData functionMetaData = new FunctionMetaData(method);

                       if (isInterfaceFunction && functionMetaData.FunctionFlags.HasFlag(EFunctionFlags.BlueprintNativeEvent))
                       {
                           throw new Exception("Interface functions cannot be marked as BlueprintEvent. Mark base declaration as BlueprintEvent instead.");
                       }

                       Functions.Add(functionMetaData);
                   }

                   if (isBlueprintOverride || isInterfaceFunction && method.GetBaseMethod().DeclaringType == _classDefinition)
                   {
                       EFunctionFlags functionFlags = EFunctionFlags.None;
                       if (isInterfaceFunction)
                       {
                           MethodDefinition interfaceFunction = FunctionMetaData.TryGetInterfaceFunction(method)!;
                           functionFlags = interfaceFunction.GetFunctionFlags();
                       }

                       VirtualFunctions.Add(new FunctionMetaData(method, false, functionFlags));
                   }
               }
           }

           private static ClassFlags GetClassFlags(TypeReference classReference, string flagsAttributeName)
           {
               return (ClassFlags) GetFlags(classReference.Resolve().CustomAttributes, flagsAttributeName);
           }

           void PopulateInterfaces()
           {
               if (_classDefinition.Interfaces.Count == 0)
               {
                   return;
               }

               Interfaces = [];

               foreach (InterfaceImplementation? typeInterface in _classDefinition.Interfaces)
               {
                   TypeDefinition interfaceType = typeInterface.InterfaceType.Resolve();

                   if (interfaceType == WeaverImporter.Instance.IInterfaceType || !interfaceType.IsUInterface())
                   {
                       continue;
                   }

                   Interfaces.Add(new TypeReferenceMetadata(interfaceType));
               }
           }

           public void PostWeaveCleanup()
           {
               foreach (FunctionMetaData function in Functions)
               {
                   function.TryRemoveMethod();
               }

               foreach (FunctionMetaData virtualFunction in VirtualFunctions)
               {
                   virtualFunction.TryRemoveMethod();
               }
           }*/
    }
}