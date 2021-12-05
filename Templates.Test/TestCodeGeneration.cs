using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.AspNetCore.Razor.Language.Intermediate;

namespace Templates.Test
{
    [TestFixture]
    public class TestCodeGeneration
    {
        [Test]
        public void GenerateCode()
        {
            var ProjectRoot = @"C:\code\razor-template-testing\Templates";

            var filePath = @"C:\code\razor-template-testing\Templates\AnotherExample.cshtml";
            var fileName = Path.GetFileName(filePath);
            var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
            var projectRelativePath = GetProjectRelativePath(filePath, ProjectRoot);
            var itemNamespace = GetNamespace(/*file, */projectRelativePath);


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
                                 builder.Features.Add(new SetNamespacePass());
                            });
            // TODO: when reading the file, need to change "stringBuilder.AppendLine($"@inherits {options.Inherits}");" at the top, otherwise the type is wrong?
            //RazorSourceDocument document = RazorSourceDocument.Create(File.ReadAllText(filePath), filePath);
            //RazorCodeDocument codeDocument2 = _projectEngine.Process(
            //    document,
            //    "mvc",
            //    null,
            //    null);
            //var cSharpDocument2 = codeDocument2.GetCSharpDocument();

            var properties = new RazorSourceDocumentProperties(filePath: filePath, relativePath: "\\" + fileName);
            RazorSourceDocument document = RazorSourceDocument.Create(File.ReadAllText(filePath), properties);

            RazorCodeDocument codeDocument1 = _projectEngine.Process(
                document,
                "mvc",
                new List<RazorSourceDocument>(),
                new List<TagHelperDescriptor>());

            var razorProjectItem = _projectEngine.FileSystem.GetItem(filePath, null);
            var codeDocument2 = _projectEngine.Process(razorProjectItem);

            RazorCSharpDocument razorCSharpDocument1 = codeDocument1.GetCSharpDocument();
            RazorCSharpDocument razorCSharpDocument2 = codeDocument2.GetCSharpDocument();

            //var result = host.GenerateCode();
            var result1 = razorCSharpDocument1.GeneratedCode;
            var result2 = razorCSharpDocument2.GeneratedCode;
        }

        private static string GetProjectRelativePath(string filePath, string projectRoot)
        {
            if (filePath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
            {
                return filePath.Substring(projectRoot.Length);
            }
            return filePath;
        }

        private static readonly Regex _namespaceRegex = new Regex(@"($|\.)(\d)", RegexOptions.Compiled);

        private string GetNamespace(/*ITaskItem file, */string projectRelativePath)
        {
            var RootNamespace= "Templates";
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
        protected override string DocumentKind { get; }

        protected override bool IsMatch(RazorCodeDocument codeDocument, DocumentIntermediateNode documentNode) => true;

        protected override void OnDocumentStructureCreated(RazorCodeDocument codeDocument, NamespaceDeclarationIntermediateNode @namespace, ClassDeclarationIntermediateNode @class, MethodDeclarationIntermediateNode method)
        {
            base.OnDocumentStructureCreated(codeDocument, @namespace, @class, method);
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
