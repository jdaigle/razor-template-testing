using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.AspNetCore.Razor.Language.Intermediate;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Templates.MSBuild
{
    public class RazorCodeGen : Task
    {
        private static readonly Regex _namespaceRegex = new Regex(@"($|\.)(\d)", RegexOptions.Compiled);
        private readonly List<ITaskItem> _generatedFiles = new List<ITaskItem>();

        public ITaskItem[] FilesToPrecompile { get; set; }

        [Required]
        public string ProjectRoot { get; set; }

        public string RootNamespace { get; set; }

        [Required]
        public string CodeGenDirectory { get; set; }

        [Output]
        public ITaskItem[] GeneratedFiles => _generatedFiles.ToArray();

        public override bool Execute()
        {
            try
            {
                return ExecuteCore();
            }
            catch (Exception ex)
            {
                Log.LogError(ex.Message);
            }
            return false;
        }

        private bool ExecuteCore()
        {
            if (FilesToPrecompile is null || !FilesToPrecompile.Any())
            {
                return true;
            }

            var projectRoot = string.IsNullOrEmpty(ProjectRoot) ? Directory.GetCurrentDirectory() : ProjectRoot;
            //using (var hostManager = new HostManager(projectRoot))
            {
                foreach (var file in FilesToPrecompile)
                {
                    var filePath = file.GetMetadata("FullPath");
                    var fileName = Path.GetFileName(filePath);
                    var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
                    var projectRelativePath = GetProjectRelativePath(filePath, projectRoot);
                    var itemNamespace = GetNamespace(/*file, */projectRelativePath);

                    //CodeLanguageUtil langutil = CodeLanguageUtil.GetLanguageUtilFromFileName(fileName);

                    var outputPath = Path.Combine(CodeGenDirectory, projectRelativePath.TrimStart(Path.DirectorySeparatorChar)) + ".cs";
                    //if (!RequiresRecompilation(filePath, outputPath))
                    //{
                    //    Log.LogMessage(MessageImportance.Low, "Skipping file {0} since {1} is already up to date", filePath, outputPath);
                    //    continue;
                    //}
                    EnsureDirectory(outputPath);

                    Log.LogMessage(MessageImportance.Normal, "Precompiling {0} at path {1}", filePath, outputPath);
                    //var host = hostManager.CreateHost(filePath, projectRelativePath, itemNamespace);

                    bool hasErrors = false;
                    //host.Error += (o, eventArgs) =>
                    //{
                    //    Log.LogError("RazorGenerator", eventArgs.ErorrCode.ToString(), helpKeyword: "", file: file.ItemSpec,
                    //                 lineNumber: (int)eventArgs.LineNumber, columnNumber: (int)eventArgs.ColumnNumber,
                    //                 endLineNumber: (int)eventArgs.LineNumber, endColumnNumber: (int)eventArgs.ColumnNumber,
                    //                 message: eventArgs.ErrorMessage);

                    //    hasErrors = true;
                    //};

                    try
                    {
                        var _projectEngine = RazorProjectEngine.Create(
                            RazorConfiguration.Default,
                            RazorProjectFileSystem.Create(ProjectRoot),
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
                                builder.ConfigureClass((c, node) =>
                                {
                                    node.ClassName = fileNameWithoutExtension;
                                });
                                //builder.SetNamespace("TemplateNamespace");
                                builder.SetRootNamespace("Templates");
                                builder.Features.Add(new SuppressChecksumOptionsFeature());
                               // builder.Features.Add(new SetNamespacePass(Log));
                            });
                        // TODO: when reading the file, need to change "stringBuilder.AppendLine($"@inherits {options.Inherits}");" at the top, otherwise the type is wrong?
                        //RazorSourceDocument document = RazorSourceDocument.Create(File.ReadAllText(filePath), filePath);
                        //RazorCodeDocument codeDocument2 = _projectEngine.Process(
                        //    document,
                        //    "mvc",
                        //    null,
                        //    null);
                        //var cSharpDocument2 = codeDocument2.GetCSharpDocument();

                        var properties = new RazorSourceDocumentProperties(filePath: filePath, relativePath: "/" + fileName);
                        RazorSourceDocument document = RazorSourceDocument.Create(File.ReadAllText(filePath), properties);

                        RazorCodeDocument codeDocument = _projectEngine.Process(
                            document,
                            "component",
                            new List<RazorSourceDocument>(),
                            new List<TagHelperDescriptor>());

                        var razorProjectItem = _projectEngine.FileSystem.GetItem(filePath, null);
                        codeDocument = _projectEngine.Process(razorProjectItem);

                        RazorCSharpDocument razorCSharpDocument = codeDocument.GetCSharpDocument();

                        //var result = host.GenerateCode();
                        var result = razorCSharpDocument.GeneratedCode;
                        if (!hasErrors)
                        {
                            // If we had errors when generating the output, don't write the file.
                            File.WriteAllText(outputPath, result);
                        }
                    }
                    catch (Exception exception)
                    {
                        Log.LogErrorFromException(exception, showStackTrace: true, showDetail: true, file: null);
                        return false;
                    }
                    if (hasErrors)
                    {
                        return false;
                    }

                    var taskItem = new TaskItem(outputPath);
                    taskItem.SetMetadata("AutoGen", "true");
                    taskItem.SetMetadata("DependentUpon", fileName);

                    _generatedFiles.Add(taskItem);
                }
            }
            return true;
        }

        /// <summary>
        /// Determines if the file has a corresponding output code-gened file that does not require updating.
        /// </summary>
        private static bool RequiresRecompilation(string filePath, string outputPath)
        {
            if (!File.Exists(outputPath))
            {
                return true;
            }
            return File.GetLastWriteTimeUtc(filePath) > File.GetLastWriteTimeUtc(outputPath);
        }

        private string GetNamespace(/*ITaskItem file, */string projectRelativePath)
        {
            projectRelativePath = Path.GetDirectoryName(projectRelativePath);

            // To keep the namespace consistent with VS, need to generate a namespace based on the folder path if no namespace is specified.
            // Also replace any non-alphanumeric characters with underscores.
            var itemNamespace = projectRelativePath.Trim(Path.DirectorySeparatorChar);
            if (string.IsNullOrEmpty(itemNamespace))
            {
                return RootNamespace;
            }

            var stringBuilder = new StringBuilder(itemNamespace.Length);
            foreach (char c in itemNamespace)
            {
                if (c == Path.DirectorySeparatorChar)
                {
                    stringBuilder.Append('.');
                }
                else if (!char.IsLetterOrDigit(c))
                {
                    stringBuilder.Append('_');
                }
                else
                {
                    stringBuilder.Append(c);
                }
            }
            itemNamespace = stringBuilder.ToString();
            itemNamespace = _namespaceRegex.Replace(itemNamespace, "$1_$2");

            if (!string.IsNullOrEmpty(RootNamespace))
            {
                itemNamespace = RootNamespace + '.' + itemNamespace;
            }

            return itemNamespace;
        }

        private static string GetProjectRelativePath(string filePath, string projectRoot)
        {
            if (filePath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
            {
                return filePath.Substring(projectRoot.Length);
            }
            return filePath;
        }

        private static void EnsureDirectory(string filePath)
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }
    }

    internal class SuppressChecksumOptionsFeature : RazorEngineFeatureBase, IConfigureRazorCodeGenerationOptionsFeature
    {
        public int Order { get; }

        public void Configure(RazorCodeGenerationOptionsBuilder options)
        {
            options.SuppressChecksum = true;
            options.SuppressMetadataAttributes = true;
        }
    }

    internal class SetNamespacePass : DocumentClassifierPassBase
    {
        private TaskLoggingHelper Log;

        public SetNamespacePass(TaskLoggingHelper log)
        {
            Log = log;
        }

        protected override string DocumentKind { get; }

        protected override bool IsMatch(RazorCodeDocument codeDocument, DocumentIntermediateNode documentNode) => true;

        protected override void OnDocumentStructureCreated(RazorCodeDocument codeDocument, NamespaceDeclarationIntermediateNode @namespace, ClassDeclarationIntermediateNode @class, MethodDeclarationIntermediateNode method)
        {
            OnDocumentStructureCreated(codeDocument, @namespace, @class, method);
            //Log.LogMessage(MessageImportance.Normal, "OnDocumentStructureCreated:");
            //Log.LogMessage(MessageImportance.Normal, "@namespace.Content=" + @namespace.Content);
            //if (codeDocument.TryComputeNamespace(fallbackToRootNamespace: true, out var ns))
            //{
            //    @namespace.Content = ns;
            //    Log.LogMessage(MessageImportance.Normal, "TryComputeNamespace=" + ns);
            //}
        }
    }
}
