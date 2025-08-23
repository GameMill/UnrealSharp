using Microsoft.Build.Framework;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;


public partial class WeaveFile : Microsoft.Build.Utilities.Task
{
    [Required] public string Source { get; set; }
    [Required] public string Output { get; set; }

    public static string FullOutputDir { get; set; }

    public static SyntaxNode root { get; set; }
    public Dictionary<string,string> StaticConstructorLines { get; set; } = new Dictionary<string, string>();
    
    public static FileLogger2 logger { get; set; }

    public static SemanticModel? semanticModel { get; set; }

    public override bool Execute()
    {

        using (logger = new FileLogger2(Path.Combine(Environment.CurrentDirectory, "weaver.txt")))
        {
            try
            {

                logger.Log($"Starting weaving: {Source} -> {Output}");

                var src = Path.GetFullPath(Source);
                var dst = Path.GetFullPath(Output);
                FullOutputDir = Path.GetDirectoryName(dst) ?? ".";






                Directory.CreateDirectory(FullOutputDir);

                var srcTime = File.GetLastWriteTimeUtc(src);
                if (File.Exists(dst))
                {
                    var dstTime = File.GetLastWriteTimeUtc(dst);
                    // Small tolerance for filesystem timestamp jitter
                    if (dstTime >= srcTime.AddMilliseconds(-2))
                    {
                        logger.LogAsync($"Up to date: {src}");
                        return true;
                    }
                }

                // TODO: Replace with real weaving logic
                SyntaxTree tree = CSharpSyntaxTree.ParseText(File.ReadAllText(src));

                // Create compilation, This compiles the file twice, once here and once in dotnet build
                var compilation = CSharpCompilation.Create("Analysis")
                    .AddReferences(
                        MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                        MetadataReference.CreateFromFile(typeof(Task).Assembly.Location), // MSBuild reference
                        MetadataReference.CreateFromFile(@"..\..\Plugins\UnrealSharp\Binaries\Managed\UnrealSharp.dll")

                    )
                    .AddSyntaxTrees(tree);

                // Get semantic model
                var semanticModel = compilation.GetSemanticModel(tree);


                root = tree.GetRoot();

                /*
                // Find all method names
                var methods = root.DescendantNodes()
                                  .OfType<MethodDeclarationSyntax>()
                                  .Select(m => m.Identifier.Text);
                foreach (var name in methods)
                    logger.Log(name);*/

                ProcessDelegates(logger);
                ProcessClasses(logger);
                ProcessEnums();

                // TODO: Replace static constructor to per class logic. Only one class per file is supported for now
                // add static constructor
                var classNodes = root.DescendantNodes().OfType<ClassDeclarationSyntax>().ToList();
                foreach (var classNode in classNodes)
                {
                    AddStaticConstructor(classNode);
                }
                logger.LogAsync($"Weaved: {src} -> {dst}");

                System.IO.File.WriteAllText(dst, root.NormalizeWhitespace().ToFullString());

                return true;
            }
            catch (Exception ex)
            {

                logger.Log(ex.ToString());
                return false;
            }
        }
    }

    public static string? FindParentDirectory(string targetName, string startPath)
    {
        var current = Path.GetFullPath(startPath);

        while (true)
        {
            var dirName = Path.GetFileName(current);
            if (string.Equals(dirName, targetName, StringComparison.OrdinalIgnoreCase))
                return current;

            var parent = Directory.GetParent(current);
            if (parent == null)
                return null;

            current = parent.FullName;
        }
    }


    public void AddPropertiesToClass(ClassDeclarationSyntax classNode, params string[] properties)
    {
        if (!root.DescendantNodes().Contains(classNode))//TODO: Unknown why, but sometimes the classNode is not the same instance as in root
        {
            var originalNode = root.DescendantNodes().OfType<ClassDeclarationSyntax>().FirstOrDefault(p => p.Identifier.Text == classNode.Identifier.Text);
            classNode = originalNode;
        }

        List<MemberDeclarationSyntax> propertySyntaxes = new List<MemberDeclarationSyntax>();
        foreach (var property in properties)
        {
            propertySyntaxes.Add(SyntaxFactory.ParseMemberDeclaration(property));
        }

        root = root.ReplaceNode(classNode, classNode.AddMembers(propertySyntaxes.ToArray()));
    }
    public string GetAssemblyName(SyntaxNode node)
    {
        // Find the first CompilationUnitSyntax ancestor
        var compilationUnit = node.Ancestors().OfType<CompilationUnitSyntax>().FirstOrDefault();
        if (compilationUnit == null)
            return string.Empty;
        // Get the assembly attribute if it exists
        var assemblyAttribute = compilationUnit.AttributeLists
            .SelectMany(al => al.Attributes)
            .FirstOrDefault(a => a.Name.ToString() == "assembly");
        if (assemblyAttribute == null)
            return string.Empty;
        // Extract the assembly name from the attribute arguments
        var nameArgument = assemblyAttribute.ArgumentList?.Arguments
            .FirstOrDefault(arg => arg.NameEquals?.Name.Identifier.Text == "Name");
        return nameArgument?.Expression.ToString().Trim('"') ?? string.Empty;
    }
    // helper to find all namespace “segments” above a node
    public string GetNamespaceDelegate(DelegateDeclarationSyntax decl)
    {
        // file-scoped namespaces (C# 10+)
        var fileScoped = decl
            .Ancestors()
            .OfType<FileScopedNamespaceDeclarationSyntax>()
            .FirstOrDefault()
            ?.Name
            .ToString();

        // regular namespace blocks (can be nested)
        var nested = decl
            .Ancestors()
            .OfType<NamespaceDeclarationSyntax>()
            .Select(n => n.Name.ToString())
            .Reverse();  // outer → inner

        // combine file-scoped + nested
        var all = (fileScoped != null
                   ? new[] { fileScoped }.Concat(nested)
                   : nested);

        return string.Join(".", all);
    }

    // helper to find all namespace “segments” above a node
    public string GetNamespaceClass(ClassDeclarationSyntax decl)
    {
        // file-scoped namespaces (C# 10+)
        var fileScoped = decl
            .Ancestors()
            .OfType<FileScopedNamespaceDeclarationSyntax>()
            .FirstOrDefault()
            ?.Name
            .ToString();

        // regular namespace blocks (can be nested)
        var nested = decl
            .Ancestors()
            .OfType<NamespaceDeclarationSyntax>()
            .Select(n => n.Name.ToString())
            .Reverse();  // outer → inner

        // combine file-scoped + nested
        var all = (fileScoped != null
                   ? new[] { fileScoped }.Concat(nested)
                   : nested);

        return string.Join(".", all);
    }


}


 

public class FileLogger : IDisposable
    {
        private readonly string _filePath;
        private readonly object _lock = new object();
        private StreamWriter _writer;

        public FileLogger(string filePath)
        {
            _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            //if (File.Exists(_filePath))
            //{
                //delete existing log file
            //    File.Delete(_filePath);
            //}

                // Ensure directory exists
                Directory.CreateDirectory(Path.GetDirectoryName(_filePath) ?? ".");

            _writer = new StreamWriter(_filePath, append: true)
            {
                AutoFlush = true
            };
        }

        public void Log(string message)
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
            lock (_lock)
            {
                _writer.WriteLine(line);
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _writer?.Dispose();
                _writer = null;
            }
        }
    }

public class FileLogger2 : IDisposable
{
    private readonly string _filePath;
    private readonly object _sync = new object();

    public FileLogger2(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentNullException(nameof(filePath));

        _filePath = filePath;
        var dir = Path.GetDirectoryName(_filePath) ?? ".";
        Directory.CreateDirectory(dir);
    }

    public void WriteJson(string message)
    {
        lock (_sync)
        {
            bool fileExists = File.Exists(_filePath);
            using (var stream = new FileStream(
                _filePath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite))
            {
                using (var writer = new StreamWriter(stream) { AutoFlush = true })
                {
                    writer.Write(fileExists? ","+Environment.NewLine+message:message);
                }
            }
        }
    }

    /// <summary>
    /// Appends a timestamped line to the log, opening and closing the file per call.
    /// Uses FileShare.ReadWrite so other processes can read or write simultaneously.
    /// </summary>
    public void Log(string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
        lock (_sync)
        {
            using (var stream = new FileStream(
                _filePath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite))
            {
                using (var writer = new StreamWriter(stream) { AutoFlush = true })
                {
                    writer.WriteLine(line);
                }
            }
        }
    }

    /// <summary>
    /// Asynchronously appends a timestamped line, minimizing caller blockage.
    /// Note: without a lock, truly concurrent calls may interleave lines.
    /// </summary>
    public async Task LogAsync(string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
        using (var stream = new FileStream(
            _filePath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.ReadWrite,
            bufferSize: 4096,
            useAsync: true))
        {
            using(var writer = new StreamWriter(stream) { AutoFlush = true })
            {
                await writer.WriteLineAsync(line).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// No persistent resources to clean up—left for compatibility.
    /// </summary>
    public void Dispose()
    {
        // nothing to dispose
    }
}





