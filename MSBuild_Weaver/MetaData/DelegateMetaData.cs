
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnrealSharp.Attributes;

namespace UnrealSharpWeaver.MetaData
{
    
    public class DelegateMetaData : TypeReferenceMetadata
    {
        public FunctionMetaData Signature { get; set; }
    
        public DelegateMetaData(DelegateDeclarationSyntax member, string attributeName = "", EFunctionFlags functionFlags = EFunctionFlags.None) : base(member, attributeName)
        {
            Name = GetUnrealDelegateNameDDS(member);
            Signature = new FunctionMetaData(member, functionFlags);
            Signature.FunctionFlags |= functionFlags;
            
        }

        public string GetUnrealDelegateName(SyntaxNode node)
        {
            // 1. Grab the TypeSyntax
            var typeSyntax = node as TypeSyntax ?? node.DescendantNodes().OfType<TypeSyntax>().FirstOrDefault() ?? throw new InvalidOperationException("No TypeSyntax found on node");

            // 2. Turn into raw text, strip generics
            //    e.g. "My.Namespace.Foo<Bar>" → "My.Namespace.Foo"
            var raw = typeSyntax.ToString().Split('<')[0].Trim();

            // 3. Replace dots with underscores
            var sanitized = raw.Replace(".", "_");

            // 4. Append Unreal delegate suffix
            return $"{sanitized}__DelegateSignature";
        }


        public static string GetUnrealDelegateNameDDS(DelegateDeclarationSyntax decl)
        {
            // 1. Collect namespace names and containing type names
            var nameParts = new Stack<string>();
            SyntaxNode? node = decl.Parent;
            while (node != null)
            {
                switch (node)
                {
                    case NamespaceDeclarationSyntax ns:
                        nameParts.Push(ns.Name.ToString());
                        break;

                    case FileScopedNamespaceDeclarationSyntax fns:
                        nameParts.Push(fns.Name.ToString());
                        break;

                    case TypeDeclarationSyntax td:
                        nameParts.Push(td.Identifier.Text);
                        break;
                }
                node = node.Parent;
            }

            // 2. Append the delegate's own identifier
            nameParts.Push(decl.Identifier.Text);

            // 3. Build the full dotted name in correct order
            var fullName = string.Join(".", nameParts.Reverse());

            // 4. Sanitize and append Unreal delegate suffix
            var sanitized = fullName.Replace(".", "_");
            return $"{sanitized}__DelegateSignature";
        }

    }
}