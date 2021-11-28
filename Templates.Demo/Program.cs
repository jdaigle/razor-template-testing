using Microsoft.AspNetCore.Razor.Language;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using RazorEngineCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Templates.Demo
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            var razorEngine = new RazorEngineCore.RazorEngine();
            var template = await razorEngine.CompileAsync(await File.ReadAllTextAsync("Example.cshtml"));

            var html = await template.RunAsync(new ExampleViewModel
            {
                Foo = "test",
                Bar = "<div>test</div>",
            });

            CompileAndEmit("Example.cshtml");
        }

        private class SuppressChecksumOptionsFeature : RazorEngineFeatureBase, IConfigureRazorCodeGenerationOptionsFeature
        {
            public int Order { get; }

            public void Configure(RazorCodeGenerationOptionsBuilder options)
            {
                options.SuppressChecksum = true;
                options.SuppressMetadataAttributes = true;
            }
        }

        public static void CompileAndEmit(string relativePath)
        {
            var _projectEngine = RazorProjectEngine.Create(
                RazorConfiguration.Default,
                RazorProjectFileSystem.Create(@"."),
                (builder) =>
                {
                    //builder.ConfigureClass((c, n) =>
                    //{
                    //    c.SetCodeGenerationOptions(RazorCodeGenerationOptions.Create(o =>
                    //    {
                    //        o.SuppressMetadataAttributes = true;
                    //        o.SuppressChecksum = true;
                    //    }));
                    //    n.BaseType = "RazorEngineCore.RazorEngineTemplateBase";
                    //});
                    builder.SetBaseType("RazorEngineCore.RazorEngineTemplateBase");
                    builder.SetNamespace("TemplateNamespace");
                    builder.Features.Add(new SuppressChecksumOptionsFeature());
                });

            var projectItem = _projectEngine.FileSystem.GetItem(relativePath, fileKind: null);
            var codeDocument = _projectEngine.Process(projectItem);
            var cSharpDocument = codeDocument.GetCSharpDocument();

            // TODO: when reading the file, need to change "stringBuilder.AppendLine($"@inherits {options.Inherits}");" at the top, otherwise the type is wrong?
            RazorSourceDocument document = RazorSourceDocument.Create(File.ReadAllText(relativePath), relativePath);
            RazorCodeDocument codeDocument2 = _projectEngine.Process(
                document,
                "mvc",
                null,
                null);
            var cSharpDocument2 = codeDocument2.GetCSharpDocument();

            if (cSharpDocument.Diagnostics.Count > 0)
            {
                throw new Exception();
                //throw CompilationFailedExceptionFactory.Create(
                //    codeDocument,
                //    cSharpDocument.Diagnostics);
            }

            var assembly = CompileAndEmit(codeDocument, cSharpDocument.GeneratedCode);

            var templateType = assembly.GetType("TemplateNamespace.Template");

            //// Anything we compile from source will use Razor 2.1 and so should have the new metadata.
            //var loader = new RazorCompiledItemLoader();
            //var item = loader.LoadItems(assembly).Single();
            //return new CompiledViewDescriptor(item);

            var instance = (RazorEngineTemplateBase<ExampleViewModel>)Activator.CreateInstance(templateType);
            instance.Model = new ExampleViewModel
            {
                Foo = "test",
                Bar = "<div>test</div>",
            };

            instance.Execute();

            var html = instance.Result();
        }

        internal static Assembly CompileAndEmit(RazorCodeDocument codeDocument, string generatedCode)
        {
            //_logger.GeneratedCodeToAssemblyCompilationStart(codeDocument.Source.FilePath);

            //var startTimestamp = _logger.IsEnabled(LogLevel.Debug) ? Stopwatch.GetTimestamp() : 0;

            var assemblyName = Path.GetRandomFileName();
            var compilation = CreateCompilation(generatedCode, assemblyName);

            var emitOptions = GetEmitOptions(); ;
            var emitPdbFile = /*_csharpCompiler.EmitPdb && emitOptions.DebugInformationFormat != DebugInformationFormat.Embedded;*/true;

            using (var assemblyStream = new MemoryStream())
            using (var pdbStream = emitPdbFile ? new MemoryStream() : null)
            {
                var result = compilation.Emit(
                    assemblyStream,
                    pdbStream,
                    options: emitOptions);

                if (!result.Success)
                {
                    throw new Exception();
                    //throw CompilationFailedExceptionFactory.Create(
                    //    codeDocument,
                    //    generatedCode,
                    //    assemblyName,
                    //    result.Diagnostics);
                }

                assemblyStream.Seek(0, SeekOrigin.Begin);
                pdbStream?.Seek(0, SeekOrigin.Begin);

                var assembly = Assembly.Load(assemblyStream.ToArray(), pdbStream?.ToArray());
                //_logger.GeneratedCodeToAssemblyCompilationEnd(codeDocument.Source.FilePath, startTimestamp);

                return assembly;
            }
        }

        private static EmitOptions GetEmitOptions(/*DependencyContextCompilationOptions dependencyContextOptions*/)
        {
            // Assume we're always producing pdbs unless DebugType = none
            //_emitPdb = true;
            DebugInformationFormat debugInformationFormat = DebugInformationFormat.Pdb;
            //if (string.IsNullOrEmpty(dependencyContextOptions.DebugType))
            //{
            //    debugInformationFormat = DebugInformationFormat.PortablePdb;
            //}
            //else
            //{
            //    // Based on https://github.com/dotnet/roslyn/blob/1d28ff9ba248b332de3c84d23194a1d7bde07e4d/src/Compilers/CSharp/Portable/CommandLine/CSharpCommandLineParser.cs#L624-L640
            //    switch (dependencyContextOptions.DebugType.ToLowerInvariant())
            //    {
            //        case "none":
            //            // There isn't a way to represent none in DebugInformationFormat.
            //            // We'll set EmitPdb to false and let callers handle it by setting a null pdb-stream.
            //            _emitPdb = false;
            //            return new EmitOptions();
            //        case "portable":
            //            debugInformationFormat = DebugInformationFormat.PortablePdb;
            //            break;
            //        case "embedded":
            //            // Roslyn does not expose enough public APIs to produce a binary with embedded pdbs.
            //            // We'll produce PortablePdb instead to continue providing a reasonable user experience.
            //            debugInformationFormat = DebugInformationFormat.PortablePdb;
            //            break;
            //        case "full":
            //        case "pdbonly":
            //            debugInformationFormat = DebugInformationFormat.PortablePdb;
            //            break;
            //        default:
            //            throw new InvalidOperationException(Resources.FormatUnsupportedDebugInformationFormat(dependencyContextOptions.DebugType));
            //    }
            //}

            var emitOptions = new EmitOptions(debugInformationFormat: debugInformationFormat);
            return emitOptions;
        }

        private static CSharpCompilation CreateCompilation(string compilationContent, string assemblyName)
        {
            var sourceText = SourceText.From(compilationContent, Encoding.UTF8);
            var syntaxTree = CreateSyntaxTree(sourceText).WithFilePath(assemblyName);
            return
                 CreateCompilation(assemblyName)
                .AddSyntaxTrees(syntaxTree);
        }

        public static SyntaxTree CreateSyntaxTree(SourceText sourceText)
        {
            return CSharpSyntaxTree.ParseText(
                sourceText,
                options: GetParseOptions());
        }

        public static CSharpCompilation CreateCompilation(string assemblyName)
        {
            var ReferencedAssemblies = new HashSet<Assembly>()
                {
                    typeof(object).Assembly,
                    Assembly.Load(new AssemblyName("Microsoft.CSharp")),
                    typeof(RazorEngineTemplateBase).Assembly,
                    Assembly.Load(new AssemblyName("System.Runtime")),
                    typeof(System.Collections.IList).Assembly,
                    typeof(System.Collections.Generic.IEnumerable<>).Assembly,
                    Assembly.Load(new AssemblyName("System.Linq")),
                    Assembly.Load(new AssemblyName("System.Linq.Expressions")),
                    Assembly.Load(new AssemblyName("netstandard")),
                    typeof(ExampleViewModel).Assembly,
                };
            var references = ReferencedAssemblies.Select(x => MetadataReference.CreateFromFile(x.Location));

            return CSharpCompilation.Create(
                assemblyName,
                options: GetCompilationOptions()
                , references: references
                //,references: _referenceManager.CompilationReferences
                );
        }

        private static CSharpParseOptions GetParseOptions(
        //IWebHostEnvironment hostingEnvironment,
        //DependencyContextCompilationOptions dependencyContextOptions
         )
        {
            //var configurationSymbol = hostingEnvironment.IsDevelopment() ? "DEBUG" : "RELEASE";
            var configurationSymbol = "DEBUG";
            //var defines = dependencyContextOptions.Defines.Concat(new[] { configurationSymbol }).Where(define => define != null);
            var defines = new[] { configurationSymbol };

            var parseOptions = new CSharpParseOptions(preprocessorSymbols: (IEnumerable<string>)defines);

            //if (string.IsNullOrEmpty(dependencyContextOptions.LanguageVersion))
            {
                // If the user does not specify a LanguageVersion, assume CSharp 8.0. This matches the language version Razor 3.0 targets by default.
                parseOptions = parseOptions.WithLanguageVersion(LanguageVersion.CSharp8);
            }
            //else if (LanguageVersionFacts.TryParse(dependencyContextOptions.LanguageVersion, out var languageVersion))
            //{
            //    parseOptions = parseOptions.WithLanguageVersion(languageVersion);
            //}
            //else
            //{
            //    Debug.Fail($"LanguageVersion {languageVersion} specified in the deps file could not be parsed.");
            //}

            return parseOptions;
        }

        private static CSharpCompilationOptions GetCompilationOptions(
        //IWebHostEnvironment hostingEnvironment,
        //DependencyContextCompilationOptions dependencyContextOptions
        )
        {
            var csharpCompilationOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);

            // Disable 1702 until roslyn turns this off by default
            csharpCompilationOptions = csharpCompilationOptions.WithSpecificDiagnosticOptions(
                new Dictionary<string, ReportDiagnostic>
                {
                    {"CS1701", ReportDiagnostic.Suppress}, // Binding redirects
                    {"CS1702", ReportDiagnostic.Suppress},
                    {"CS1705", ReportDiagnostic.Suppress}
                });

            //if (dependencyContextOptions.AllowUnsafe.HasValue)
            //{
            //    csharpCompilationOptions = csharpCompilationOptions.WithAllowUnsafe(
            //        dependencyContextOptions.AllowUnsafe.Value);
            //}

            OptimizationLevel optimizationLevel;
            //if (dependencyContextOptions.Optimize.HasValue)
            //{
            //    optimizationLevel = dependencyContextOptions.Optimize.Value ?
            //        OptimizationLevel.Release :
            //        OptimizationLevel.Debug;
            //}
            //else
            {
                //optimizationLevel = hostingEnvironment.IsDevelopment() ?
                //    OptimizationLevel.Debug :
                //    OptimizationLevel.Release;
                optimizationLevel = OptimizationLevel.Debug;
            }
            csharpCompilationOptions = csharpCompilationOptions.WithOptimizationLevel(optimizationLevel);

            //if (dependencyContextOptions.WarningsAsErrors.HasValue)
            //{
            //    var reportDiagnostic = dependencyContextOptions.WarningsAsErrors.Value ?
            //        ReportDiagnostic.Error :
            //        ReportDiagnostic.Default;
            //    csharpCompilationOptions = csharpCompilationOptions.WithGeneralDiagnosticOption(reportDiagnostic);
            //}

            return csharpCompilationOptions;
        }
    }
}
