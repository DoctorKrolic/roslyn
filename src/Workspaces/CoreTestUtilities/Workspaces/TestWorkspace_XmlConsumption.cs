﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable disable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Xml.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Editor.UnitTests.Extensions;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.VisualStudio.Composition;
using Roslyn.Test.Utilities;
using Roslyn.Test.Utilities.TestGenerators;
using Roslyn.Utilities;
using Xunit;

namespace Microsoft.CodeAnalysis.Test.Utilities;

public partial class TestWorkspace<TDocument, TProject, TSolution>
{
    private const string CSharpExtension = ".cs";
    private const string CSharpScriptExtension = ".csx";
    private const string VisualBasicExtension = ".vb";
    private const string VisualBasicScriptExtension = ".vbx";

    private const string WorkspaceElementName = "Workspace";
    private const string ProjectElementName = "Project";
    private const string SubmissionElementName = "Submission";
    private const string MetadataReferenceElementName = "MetadataReference";
    private const string MetadataReferenceFromSourceElementName = "MetadataReferenceFromSource";
    private const string ProjectReferenceElementName = "ProjectReference";
    private const string CompilationOptionsElementName = "CompilationOptions";
    private const string RootNamespaceAttributeName = "RootNamespace";
    private const string OutputTypeAttributeName = "OutputType";
    private const string ReportDiagnosticAttributeName = "ReportDiagnostic";
    private const string CryptoKeyFileAttributeName = "CryptoKeyFile";
    private const string StrongNameProviderAttributeName = "StrongNameProvider";
    private const string DelaySignAttributeName = "DelaySign";
    private const string ParseOptionsElementName = "ParseOptions";
    private const string LanguageVersionAttributeName = "LanguageVersion";
    private const string FeaturesAttributeName = "Features";
    private const string DocumentationModeAttributeName = "DocumentationMode";
    private const string DocumentElementName = "Document";
    private const string AdditionalDocumentElementName = "AdditionalDocument";
    private const string AnalyzerConfigDocumentElementName = "AnalyzerConfigDocument";
    private const string AnalyzerElementName = "Analyzer";
    private const string AssemblyNameAttributeName = "AssemblyName";
    private const string CommonReferencesAttributeName = "CommonReferences";
    private const string CommonReferencesWithoutValueTupleAttributeName = "CommonReferencesWithoutValueTuple";
    private const string CommonReferencesWinRTAttributeName = "CommonReferencesWinRT";
    private const string CommonReferencesNet45AttributeName = "CommonReferencesNet45";
    private const string CommonReferencesPortableAttributeName = "CommonReferencesPortable";
    private const string CommonReferencesNetCoreAppName = "CommonReferencesNetCoreApp";
    private const string CommonReferencesNet6Name = "CommonReferencesNet6";
    private const string CommonReferencesNet7Name = "CommonReferencesNet7";
    private const string CommonReferencesNet8Name = "CommonReferencesNet8";
    private const string CommonReferencesNet9Name = "CommonReferencesNet9";
    private const string CommonReferencesNetStandard20Name = "CommonReferencesNetStandard20";
    private const string CommonReferencesMinCorlibName = "CommonReferencesMinCorlib";
    private const string FilePathAttributeName = "FilePath";
    private const string FoldersAttributeName = "Folders";
    private const string KindAttributeName = "Kind";
    private const string LanguageAttributeName = "Language";
    private const string GlobalImportElementName = "GlobalImport";
    private const string IncludeXmlDocCommentsAttributeName = "IncludeXmlDocComments";
    private const string IsLinkFileAttributeName = "IsLinkFile";
    private const string LinkAssemblyNameAttributeName = "LinkAssemblyName";
    private const string LinkProjectNameAttributeName = "LinkProjectName";
    private const string LinkFilePathAttributeName = "LinkFilePath";
    private const string MarkupAttributeName = "Markup";
    private const string NormalizeAttributeName = "Normalize";
    private const string PreprocessorSymbolsAttributeName = "PreprocessorSymbols";
    private const string AnalyzerDisplayAttributeName = "Name";
    private const string AnalyzerFullPathAttributeName = "FullPath";
    private const string AliasAttributeName = "Alias";
    private const string ProjectNameAttribute = "Name";
    private const string CheckOverflowAttributeName = "CheckOverflow";
    private const string AllowUnsafeAttributeName = "AllowUnsafe";
    private const string OutputKindName = "OutputKind";
    private const string NullableAttributeName = "Nullable";
    private const string DocumentFromSourceGeneratorElementName = "DocumentFromSourceGenerator";

    /// <summary>
    /// This place-holder value is used to set a project's file path to be null.  It was explicitly chosen to be
    /// convoluted to avoid any accidental usage (e.g., what if I really wanted FilePath to be the string "null"?),
    /// obvious to anybody debugging that it is a special value, and invalid as an actual file path.
    /// </summary>
    public const string NullFilePath = "NullFilePath::{AFA13775-BB7D-4020-9E58-C68CF43D8A68}";

    private sealed class TestDocumentationProvider : DocumentationProvider
    {
        protected override string GetDocumentationForSymbol(string documentationMemberID, CultureInfo preferredCulture, CancellationToken cancellationToken = default)
            => string.Format("<member name='{0}'><summary>{0}</summary></member>", documentationMemberID);

        public override bool Equals(object obj)
            => ReferenceEquals(this, obj);

        public override int GetHashCode()
            => RuntimeHelpers.GetHashCode(this);
    }

    private TProject CreateProject(
        XElement workspaceElement,
        XElement projectElement,
        ExportProvider exportProvider,
        IDocumentServiceProvider documentServiceProvider,
        ref int projectId,
        ref int documentId)
    {
        AssertNoChildText(projectElement);

        var language = GetLanguage(projectElement);

        var assemblyName = GetAssemblyName(projectElement, ref projectId);

        string projectFilePath;

        var projectName = projectElement.Attribute(ProjectNameAttribute)?.Value ?? assemblyName;

        if (projectElement.Attribute(FilePathAttributeName) != null)
        {
            projectFilePath = projectElement.Attribute(FilePathAttributeName).Value;
            if (string.Compare(projectFilePath, NullFilePath, StringComparison.Ordinal) == 0)
            {
                // allow explicit null file path
                projectFilePath = null;
            }
        }
        else
        {
            projectFilePath = projectName +
                (language == LanguageNames.CSharp ? ".csproj" :
                 language == LanguageNames.VisualBasic ? ".vbproj" : ("." + language));
        }

        if (projectFilePath != null)
        {
            projectFilePath = PathUtilities.CombinePaths(TestWorkspace.RootDirectory, projectFilePath);
        }

        var projectOutputDir = AbstractTestHostProject.GetTestOutputDirectory(projectFilePath);

        var languageServices = Services.GetLanguageServices(language);

        var parseOptions = GetParseOptions(projectElement, language, languageServices);
        var compilationOptions = CreateCompilationOptions(projectElement, language, parseOptions);
        var rootNamespace = GetRootNamespace(compilationOptions, projectElement);

        var references = CreateReferenceList(projectElement);
        var analyzers = CreateAnalyzerList(projectElement);

        var documents = new List<TDocument>();
        var documentElements = projectElement.Elements(DocumentElementName).ToList();
        foreach (var documentElement in documentElements)
        {
            var document = CreateDocument(
                workspaceElement,
                documentElement,
                exportProvider,
                languageServices,
                documentServiceProvider,
                ref documentId);

            documents.Add(document);
        }

        SingleFileTestGenerator testGenerator = null;
        foreach (var sourceGeneratedDocumentElement in projectElement.Elements(DocumentFromSourceGeneratorElementName))
        {
            if (testGenerator is null)
            {
                testGenerator = new SingleFileTestGenerator();
                analyzers.Add(new TestGeneratorReference(testGenerator));
            }

            var name = GetFileName(sourceGeneratedDocumentElement, ref documentId);

            var markupCode = (bool?)sourceGeneratedDocumentElement.Attribute(NormalizeAttributeName) is false
                ? sourceGeneratedDocumentElement.Value
                : sourceGeneratedDocumentElement.NormalizedValue();
            MarkupTestFile.GetPositionAndSpans(markupCode,
                out var code, out var cursorPosition, out IDictionary<string, ImmutableArray<TextSpan>> spans);

            var documentFilePath = Path.Combine(projectOutputDir, "obj", typeof(SingleFileTestGenerator).Assembly.GetName().Name, typeof(SingleFileTestGenerator).FullName, name);
            var document = CreateDocument(exportProvider, languageServices, code, name, documentFilePath, cursorPosition, spans, generator: testGenerator);
            documents.Add(document);

            testGenerator.AddSource(code, name);
        }

        var additionalDocuments = new List<TDocument>();
        var additionalDocumentElements = projectElement.Elements(AdditionalDocumentElementName).ToList();
        foreach (var additionalDocumentElement in additionalDocumentElements)
        {
            var document = CreateDocument(
                workspaceElement,
                additionalDocumentElement,
                exportProvider,
                languageServices,
                documentServiceProvider,
                ref documentId);

            additionalDocuments.Add(document);
        }

        var analyzerConfigDocuments = new List<TDocument>();
        var analyzerConfigElements = projectElement.Elements(AnalyzerConfigDocumentElementName).ToList();
        foreach (var analyzerConfigElement in analyzerConfigElements)
        {
            var document = CreateDocument(
                workspaceElement,
                analyzerConfigElement,
                exportProvider,
                languageServices,
                documentServiceProvider,
                ref documentId);

            analyzerConfigDocuments.Add(document);
        }

        return CreateProject(languageServices, compilationOptions, parseOptions, assemblyName, projectName, references, documents, additionalDocuments, analyzerConfigDocuments, filePath: projectFilePath, analyzerReferences: analyzers, defaultNamespace: rootNamespace);
    }

    private static ParseOptions GetParseOptions(XElement projectElement, string language, HostLanguageServices languageServices)
    {
        return language is LanguageNames.CSharp or LanguageNames.VisualBasic
            ? GetParseOptionsWorker(projectElement, language, languageServices)
            : null;
    }

    private static ParseOptions GetParseOptionsWorker(XElement projectElement, string language, HostLanguageServices languageServices)
    {
        ParseOptions parseOptions;
        var preprocessorSymbolsAttribute = projectElement.Attribute(PreprocessorSymbolsAttributeName);
        if (preprocessorSymbolsAttribute != null)
        {
            parseOptions = GetPreProcessorParseOptions(language, preprocessorSymbolsAttribute);
        }
        else
        {
            parseOptions = languageServices.GetService<ISyntaxTreeFactoryService>().GetDefaultParseOptions();
        }

        var languageVersionAttribute = projectElement.Attribute(LanguageVersionAttributeName);
        if (languageVersionAttribute != null)
        {
            parseOptions = GetParseOptionsWithLanguageVersion(language, parseOptions, languageVersionAttribute);
        }

        var featuresAttribute = projectElement.Attribute(FeaturesAttributeName);
        if (featuresAttribute != null)
        {
            parseOptions = GetParseOptionsWithFeatures(parseOptions, featuresAttribute);
        }

        var documentationMode = GetDocumentationMode(projectElement);
        if (documentationMode != null)
        {
            parseOptions = parseOptions.WithDocumentationMode(documentationMode.Value);
        }

        return parseOptions;
    }

    private static ParseOptions GetPreProcessorParseOptions(string language, XAttribute preprocessorSymbolsAttribute)
    {
        if (language == LanguageNames.CSharp)
        {
            return new CSharpParseOptions(preprocessorSymbols: preprocessorSymbolsAttribute.Value.Split(','));
        }
        else if (language == LanguageNames.VisualBasic)
        {
            return new VisualBasicParseOptions(preprocessorSymbols: preprocessorSymbolsAttribute.Value
                .Split(',').Select(v => KeyValuePair.Create(v.Split('=').ElementAt(0), (object)v.Split('=').ElementAt(1))).ToImmutableArray());
        }
        else
        {
            throw new ArgumentException("Unexpected language '{0}' for generating custom parse options.", language);
        }
    }

    private static ParseOptions GetParseOptionsWithFeatures(ParseOptions parseOptions, XAttribute featuresAttribute)
    {
        var entries = featuresAttribute.Value.Split(';');
        var features = entries.Select(x =>
        {
            var split = x.Split('=');

            var key = split[0];
            var value = split.Length == 2 ? split[1] : "true";

            return KeyValuePair.Create(key, value);
        });

        return parseOptions.WithFeatures(features);
    }

    private static ParseOptions GetParseOptionsWithLanguageVersion(string language, ParseOptions parseOptions, XAttribute languageVersionAttribute)
    {
        if (language == LanguageNames.CSharp)
        {
            if (CodeAnalysis.CSharp.LanguageVersionFacts.TryParse(languageVersionAttribute.Value, out var languageVersion))
            {
                return ((CSharpParseOptions)parseOptions).WithLanguageVersion(languageVersion);
            }
        }
        else if (language == LanguageNames.VisualBasic)
        {
            var languageVersion = CodeAnalysis.VisualBasic.LanguageVersion.Default;
            if (CodeAnalysis.VisualBasic.LanguageVersionFacts.TryParse(languageVersionAttribute.Value, ref languageVersion))
            {
                return ((VisualBasicParseOptions)parseOptions).WithLanguageVersion(languageVersion);
            }
        }

        throw new Exception($"LanguageVersion attribute on {languageVersionAttribute.Parent} was not recognized.");
    }

    private static DocumentationMode? GetDocumentationMode(XElement projectElement)
    {
        var documentationModeAttribute = projectElement.Attribute(DocumentationModeAttributeName);
        if (documentationModeAttribute != null)
        {
            return (DocumentationMode)Enum.Parse(typeof(DocumentationMode), documentationModeAttribute.Value);
        }
        else
        {
            return null;
        }
    }

    private string GetAssemblyName(XElement projectElement, ref int projectId)
    {
        var assemblyNameAttribute = projectElement.Attribute(AssemblyNameAttributeName);
        if (assemblyNameAttribute != null)
        {
            return assemblyNameAttribute.Value;
        }

        var language = GetLanguage(projectElement);

        projectId++;
        return language == LanguageNames.CSharp ? "CSharpAssembly" + projectId :
               language == LanguageNames.VisualBasic ? "VisualBasicAssembly" + projectId :
                                                        language + "Assembly" + projectId;
    }

    private string GetLanguage(XElement projectElement)
    {
        var languageAttribute = projectElement.Attribute(LanguageAttributeName);
        if (languageAttribute == null)
        {
            throw new ArgumentException($"{projectElement} is missing a {LanguageAttributeName} attribute.");
        }

        var languageName = languageAttribute.Value;

        if (!Services.SupportedLanguages.Contains(languageName))
        {
            throw new ArgumentException(string.Format("Language should be one of '{0}' and it is {1}",
                string.Join(", ", Services.SupportedLanguages),
                languageName));
        }

        return languageName;
    }

    private string GetRootNamespace(CompilationOptions compilationOptions, XElement projectElement)
    {
        var rootNamespaceAttribute = projectElement.Attribute(RootNamespaceAttributeName);

        if (GetLanguage(projectElement) == LanguageNames.VisualBasic)
        {
            // For VB tests, root namespace value must be defined in compilation options element,
            // it can't use the property in project element to avoid confusion.
            Assert.Null(rootNamespaceAttribute);

            var vbCompilationOptions = (VisualBasicCompilationOptions)compilationOptions;
            return vbCompilationOptions.RootNamespace;
        }

        // If it's not defined, default to "" (global namespace)
        return rootNamespaceAttribute?.Value ?? string.Empty;
    }

    private CompilationOptions CreateCompilationOptions(
        XElement projectElement,
        string language,
        ParseOptions parseOptions)
    {
        var compilationOptionsElement = projectElement.Element(CompilationOptionsElementName);
        return language is LanguageNames.CSharp or LanguageNames.VisualBasic
            ? CreateCompilationOptions(language, compilationOptionsElement, parseOptions)
            : null;
    }

    private CompilationOptions CreateCompilationOptions(string language, XElement compilationOptionsElement, ParseOptions parseOptions)
    {
        var rootNamespace = new VisualBasicCompilationOptions(OutputKind.ConsoleApplication).RootNamespace;
        var globalImports = new List<GlobalImport>();
        var reportDiagnostic = ReportDiagnostic.Default;
        var cryptoKeyFile = (string)null;
        var strongNameProvider = (StrongNameProvider)null;
        var delaySign = (bool?)null;
        var checkOverflow = false;
        var allowUnsafe = false;
        var outputKind = OutputKind.DynamicallyLinkedLibrary;
        var nullable = NullableContextOptions.Disable;

        if (compilationOptionsElement != null)
        {
            globalImports = [.. compilationOptionsElement.Elements(GlobalImportElementName).Select(x => GlobalImport.Parse(x.Value))];
            var rootNamespaceAttribute = compilationOptionsElement.Attribute(RootNamespaceAttributeName);
            if (rootNamespaceAttribute != null)
            {
                rootNamespace = rootNamespaceAttribute.Value;
            }

            var outputKindAttribute = compilationOptionsElement.Attribute(OutputKindName);
            if (outputKindAttribute != null)
            {
                outputKind = (OutputKind)Enum.Parse(typeof(OutputKind), outputKindAttribute.Value);
            }

            var checkOverflowAttribute = compilationOptionsElement.Attribute(CheckOverflowAttributeName);
            if (checkOverflowAttribute != null)
            {
                checkOverflow = (bool)checkOverflowAttribute;
            }

            var allowUnsafeAttribute = compilationOptionsElement.Attribute(AllowUnsafeAttributeName);
            if (allowUnsafeAttribute != null)
            {
                allowUnsafe = (bool)allowUnsafeAttribute;
            }

            var reportDiagnosticAttribute = compilationOptionsElement.Attribute(ReportDiagnosticAttributeName);
            if (reportDiagnosticAttribute != null)
            {
                reportDiagnostic = (ReportDiagnostic)Enum.Parse(typeof(ReportDiagnostic), (string)reportDiagnosticAttribute);
            }

            var cryptoKeyFileAttribute = compilationOptionsElement.Attribute(CryptoKeyFileAttributeName);
            if (cryptoKeyFileAttribute != null)
            {
                cryptoKeyFile = (string)cryptoKeyFileAttribute;
            }

            var strongNameProviderAttribute = compilationOptionsElement.Attribute(StrongNameProviderAttributeName);
            if (strongNameProviderAttribute != null)
            {
                var type = Type.GetType((string)strongNameProviderAttribute);
                // DesktopStrongNameProvider and SigningTestHelpers.VirtualizedStrongNameProvider do
                // not have a default constructor but constructors with optional parameters.
                // Activator.CreateInstance does not work with this.
                if (type == typeof(DesktopStrongNameProvider))
                {
                    strongNameProvider = SigningTestHelpers.DefaultDesktopStrongNameProvider;
                }
                else
                {
                    strongNameProvider = (StrongNameProvider)Activator.CreateInstance(type);
                }
            }

            var delaySignAttribute = compilationOptionsElement.Attribute(DelaySignAttributeName);
            if (delaySignAttribute != null)
            {
                delaySign = (bool)delaySignAttribute;
            }

            var nullableAttribute = compilationOptionsElement.Attribute(NullableAttributeName);
            if (nullableAttribute != null)
            {
                nullable = (NullableContextOptions)Enum.Parse(typeof(NullableContextOptions), nullableAttribute.Value);
            }

            var outputTypeAttribute = compilationOptionsElement.Attribute(OutputTypeAttributeName);
            if (outputTypeAttribute != null
                && outputTypeAttribute.Value == "WindowsRuntimeMetadata")
            {
                if (rootNamespaceAttribute == null)
                {
                    rootNamespace = new VisualBasicCompilationOptions(OutputKind.WindowsRuntimeMetadata).RootNamespace;
                }

                // VB needs Compilation.ParseOptions set (we do the same at the VS layer)
                return language == LanguageNames.CSharp
                   ? new CSharpCompilationOptions(OutputKind.WindowsRuntimeMetadata, allowUnsafe: allowUnsafe)
                   : new VisualBasicCompilationOptions(OutputKind.WindowsRuntimeMetadata).WithGlobalImports(globalImports).WithRootNamespace(rootNamespace)
                        .WithParseOptions((VisualBasicParseOptions)parseOptions ?? VisualBasicParseOptions.Default);
            }
        }
        else
        {
            // Add some common global imports by default for VB
            globalImports.Add(GlobalImport.Parse("System"));
            globalImports.Add(GlobalImport.Parse("System.Collections.Generic"));
            globalImports.Add(GlobalImport.Parse("System.Linq"));
        }

        // TODO: Allow these to be specified.
        var languageServices = Services.GetLanguageServices(language);
        var metadataService = Services.GetService<IMetadataService>();
        var compilationOptions = languageServices.GetService<ICompilationFactoryService>().GetDefaultCompilationOptions();
        compilationOptions = compilationOptions.WithOutputKind(outputKind)
                                               .WithGeneralDiagnosticOption(reportDiagnostic)
                                               .WithSourceReferenceResolver(SourceFileResolver.Default)
                                               .WithXmlReferenceResolver(XmlFileResolver.Default)
                                               .WithMetadataReferenceResolver(new WorkspaceMetadataFileReferenceResolver(metadataService, new RelativePathResolver([], null)))
                                               .WithAssemblyIdentityComparer(DesktopAssemblyIdentityComparer.Default)
                                               .WithCryptoKeyFile(cryptoKeyFile)
                                               .WithStrongNameProvider(strongNameProvider)
                                               .WithDelaySign(delaySign)
                                               .WithOverflowChecks(checkOverflow);

        if (language == LanguageNames.CSharp)
        {
            compilationOptions = ((CSharpCompilationOptions)compilationOptions).WithAllowUnsafe(allowUnsafe).WithNullableContextOptions(nullable);
        }

        if (language == LanguageNames.VisualBasic)
        {
            // VB needs Compilation.ParseOptions set (we do the same at the VS layer)
            compilationOptions = ((VisualBasicCompilationOptions)compilationOptions).WithRootNamespace(rootNamespace)
                                                                                    .WithGlobalImports(globalImports)
                                                                                    .WithParseOptions((VisualBasicParseOptions)parseOptions ??
                                                                                        VisualBasicParseOptions.Default);
        }

        return compilationOptions;
    }

    private TDocument CreateDocument(
        XElement workspaceElement,
        XElement documentElement,
        ExportProvider exportProvider,
        HostLanguageServices languageServiceProvider,
        IDocumentServiceProvider documentServiceProvider,
        ref int documentId)
    {
        var isLinkFileAttribute = documentElement.Attribute(IsLinkFileAttributeName);
        var isLinkFile = isLinkFileAttribute != null && ((bool?)isLinkFileAttribute).HasValue && ((bool?)isLinkFileAttribute).Value;
        if (isLinkFile)
        {
            // This is a linked file. Use the filePath and markup from the referenced document.

            var originalAssemblyName = documentElement.Attribute(LinkAssemblyNameAttributeName)?.Value;
            var originalProjectName = documentElement.Attribute(LinkProjectNameAttributeName)?.Value;

            if (originalAssemblyName == null && originalProjectName == null)
            {
                throw new ArgumentException($"Linked files must specify either a {LinkAssemblyNameAttributeName} or {LinkProjectNameAttributeName}");
            }

            var originalProject = workspaceElement.Elements(ProjectElementName).FirstOrDefault(p =>
            {
                if (originalAssemblyName != null)
                {
                    return p.Attribute(AssemblyNameAttributeName)?.Value == originalAssemblyName;
                }
                else
                {
                    return p.Attribute(ProjectNameAttribute)?.Value == originalProjectName;
                }
            });

            if (originalProject == null)
            {
                if (originalProjectName != null)
                {
                    throw new ArgumentException($"Linked file's {LinkProjectNameAttributeName} '{originalProjectName}' project not found.");
                }
                else
                {
                    throw new ArgumentException($"Linked file's {LinkAssemblyNameAttributeName} '{originalAssemblyName}' project not found.");
                }
            }

            var originalDocumentPath = documentElement.Attribute(LinkFilePathAttributeName)?.Value;

            if (originalDocumentPath == null)
            {
                throw new ArgumentException($"Linked files must specify a {LinkFilePathAttributeName}");
            }

            documentElement = originalProject.Elements(DocumentElementName).FirstOrDefault(d =>
            {
                return d.Attribute(FilePathAttributeName)?.Value == originalDocumentPath;
            });

            if (documentElement == null)
            {
                throw new ArgumentException($"Linked file's LinkFilePath '{originalDocumentPath}' file not found.");
            }
        }

        var markupCode = (bool?)documentElement.Attribute(NormalizeAttributeName) is false
            ? documentElement.Value
            : documentElement.NormalizedValue();

        var fileName = GetFileName(documentElement, ref documentId);

        var folders = GetFolders(documentElement);
        var optionsElement = documentElement.Element(ParseOptionsElementName);

        // TODO: Allow these to be specified.
        var codeKind = SourceCodeKind.Regular;
        if (optionsElement != null)
        {
            var attr = optionsElement.Attribute(KindAttributeName);
            codeKind = attr == null
                ? SourceCodeKind.Regular
                : (SourceCodeKind)Enum.Parse(typeof(SourceCodeKind), attr.Value);
        }

        var markupAttribute = documentElement.Attribute(MarkupAttributeName);
        var isMarkup = markupAttribute == null || (string)markupAttribute == "true" || (string)markupAttribute == "SpansOnly";

        string code;
        int? cursorPosition;
        ImmutableDictionary<string, ImmutableArray<TextSpan>> spans;

        if (isMarkup)
        {
            // if the caller doesn't want us caring about positions, then replace any $'s with a character unlikely
            // to ever show up in the doc naturally.  Then, after we convert things, change that character back. We
            // do this as a single character so that all the positions of the spans do not change.
            if ((string)markupAttribute == "SpansOnly")
                markupCode = markupCode.Replace("$", "\uD7FF");

            TestFileMarkupParser.GetPositionAndSpans(markupCode, out code, out cursorPosition, out spans);

            // if we were told SpansOnly then that means that $$ isn't actually a caret (but is something like a raw
            // interpolated string delimiter.  In that case, if we did see a $$ add it back it at the location we
            // found it, and set the cursor back to null as the test will be specifying that location manually
            // itself.
            if ((string)markupAttribute == "SpansOnly")
            {
                Contract.ThrowIfTrue(cursorPosition != null);
                code = code.Replace("\uD7FF", "$");
            }
        }
        else
        {
            code = markupCode;
            cursorPosition = null;
            spans = ImmutableDictionary<string, ImmutableArray<TextSpan>>.Empty;
        }

        var testDocumentServiceProvider = GetDocumentServiceProvider(documentElement);

        if (documentServiceProvider == null)
        {
            documentServiceProvider = testDocumentServiceProvider;
        }
        else if (testDocumentServiceProvider != null)
        {
            AssertEx.Fail($"The document attributes on file {fileName} conflicted");
        }

        var filePath = Path.Combine(TestWorkspace.RootDirectory, fileName);

        return CreateDocument(
            exportProvider, languageServiceProvider, code, fileName, filePath, cursorPosition, spans, codeKind, folders, isLinkFile, documentServiceProvider);
    }
#nullable enable

    private static TestDocumentServiceProvider? GetDocumentServiceProvider(XElement documentElement)
    {
        var canApplyChange = (bool?)documentElement.Attribute("CanApplyChange");
        var supportDiagnostics = (bool?)documentElement.Attribute("SupportDiagnostics");

        if (canApplyChange == null && supportDiagnostics == null)
        {
            return null;
        }

        return new TestDocumentServiceProvider(
            canApplyChange ?? true,
            supportDiagnostics ?? true);
    }

#nullable disable

    private string GetFileName(XElement documentElement, ref int documentId)
    {
        var filePathAttribute = documentElement.Attribute(FilePathAttributeName);
        if (filePathAttribute != null)
        {
            return filePathAttribute.Value;
        }

        var language = GetLanguage(documentElement.Ancestors(ProjectElementName).Single());
        documentId++;
        var name = "Test" + documentId;
        return language == LanguageNames.CSharp ? name + ".cs" : name + ".vb";
    }

    private static IReadOnlyList<string> GetFolders(XElement documentElement)
    {
        var folderAttribute = documentElement.Attribute(FoldersAttributeName);
        if (folderAttribute == null)
        {
            return null;
        }

        var folderContainers = folderAttribute.Value.Split([PathUtilities.DirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        return new ReadOnlyCollection<string>([.. folderContainers]);
    }

    /// <summary>
    /// Takes completely valid code, compiles it, and emits it to a MetadataReference without using
    /// the file system
    /// </summary>
    protected virtual (MetadataReference reference, ImmutableArray<byte> peImage) CreateMetadataReferenceFromSource(XElement projectElement, XElement referencedSource)
    {
        var compilation = CreateCompilation(referencedSource);

        var aliasElement = referencedSource.Attribute("Aliases")?.Value;
        var aliases = aliasElement != null ? aliasElement.Split(',').Select(s => s.Trim()).ToImmutableArray() : default;

        var includeXmlDocComments = false;
        var includeXmlDocCommentsAttribute = referencedSource.Attribute(IncludeXmlDocCommentsAttributeName);
        if (includeXmlDocCommentsAttribute != null &&
            ((bool?)includeXmlDocCommentsAttribute).HasValue &&
            ((bool?)includeXmlDocCommentsAttribute).Value)
        {
            includeXmlDocComments = true;
        }

        var image = compilation.EmitToArray();
        var metadataReference = MetadataReference.CreateFromImage(image, new MetadataReferenceProperties(aliases: aliases), includeXmlDocComments ? new DeferredDocumentationProvider(compilation) : null);
        return (metadataReference, image);
    }

    private Compilation CreateCompilation(XElement referencedSource)
    {
        AssertNoChildText(referencedSource);

        var languageName = GetLanguage(referencedSource);

        var assemblyName = "ReferencedAssembly";
        var assemblyNameAttribute = referencedSource.Attribute(AssemblyNameAttributeName);
        if (assemblyNameAttribute != null)
        {
            assemblyName = assemblyNameAttribute.Value;
        }

        var languageServices = Services.GetLanguageServices(languageName);
        var compilationFactory = languageServices.GetService<ICompilationFactoryService>();
        var options = compilationFactory.GetDefaultCompilationOptions().WithOutputKind(OutputKind.DynamicallyLinkedLibrary);

        var compilation = compilationFactory.CreateCompilation(assemblyName, options);

        var documentElements = referencedSource.Elements(DocumentElementName).ToList();
        var parseOptions = GetParseOptions(referencedSource, languageName, languageServices);

        foreach (var documentElement in documentElements)
        {
            compilation = compilation.AddSyntaxTrees(CreateSyntaxTree(parseOptions, documentElement.Value));
        }

        foreach (var reference in CreateReferenceList(referencedSource))
        {
            compilation = compilation.AddReferences(reference);
        }

        return compilation;
    }

    private static SyntaxTree CreateSyntaxTree(ParseOptions options, string referencedCode)
    {
        var sourceText = SourceText.From(referencedCode, encoding: null, SourceHashAlgorithms.Default);

        if (LanguageNames.CSharp == options.Language)
        {
            return Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseSyntaxTree(sourceText, options);
        }
        else
        {
            return Microsoft.CodeAnalysis.VisualBasic.SyntaxFactory.ParseSyntaxTree(sourceText, options);
        }
    }

    private IList<MetadataReference> CreateReferenceList(XElement element)
    {
        var references = CreateCommonReferences(element);
        foreach (var reference in element.Elements(MetadataReferenceElementName))
        {
            // Read the image to an ImmutableArray<byte>, since the GC does a better job of tracking these than
            // Marshal.AllocHGlobal and thus knowing when it's necessary to run finalizers to clean up old Metadata
            // objects that are no longer in use. There are no public APIs available to directly dispose of these
            // images, so we are relying on GC running finalizers to avoid OutOfMemoryException during tests.
            var content = File.ReadAllBytes(reference.Value);
            references.Add(MetadataReference.CreateFromImage(content, filePath: reference.Value));
        }

        foreach (var metadataReferenceFromSource in element.Elements(MetadataReferenceFromSourceElementName))
        {
            references.Add(CreateMetadataReferenceFromSource(element, metadataReferenceFromSource).reference);
        }

        return references;
    }

    private static IList<AnalyzerReference> CreateAnalyzerList(XElement projectElement)
    {
        var analyzers = new List<AnalyzerReference>();
        foreach (var analyzer in projectElement.Elements(AnalyzerElementName))
        {
            analyzers.Add(
                new AnalyzerImageReference(
                    [],
                    display: (string)analyzer.Attribute(AnalyzerDisplayAttributeName),
                    fullPath: (string)analyzer.Attribute(AnalyzerFullPathAttributeName)));
        }

        return analyzers;
    }

    private IList<MetadataReference> CreateCommonReferences(XElement element)
    {
        var references = new List<MetadataReference>();

        var net45 = element.Attribute(CommonReferencesNet45AttributeName);
        if (net45 != null &&
            ((bool?)net45).HasValue &&
            ((bool?)net45).Value)
        {
            references = [TestBase.MscorlibRef_v4_0_30316_17626, TestBase.SystemRef_v4_0_30319_17929, TestBase.SystemCoreRef_v4_0_30319_17929, TestBase.SystemRuntimeSerializationRef_v4_0_30319_17929];
            if (GetLanguage(element) == LanguageNames.VisualBasic)
            {
                references.Add(TestBase.MsvbRef);
                references.Add(TestBase.SystemXmlRef);
                references.Add(TestBase.SystemXmlLinqRef);
            }
        }

        var commonReferencesAttribute = element.Attribute(CommonReferencesAttributeName);
        if (commonReferencesAttribute != null &&
            ((bool?)commonReferencesAttribute).HasValue &&
            ((bool?)commonReferencesAttribute).Value)
        {
            references = [TestBase.MscorlibRef_v46, TestBase.SystemRef_v46, TestBase.SystemCoreRef_v46, TestBase.ValueTupleRef, TestBase.SystemRuntimeFacadeRef];
            if (GetLanguage(element) == LanguageNames.VisualBasic)
            {
                references.Add(TestBase.MsvbRef_v4_0_30319_17929);
                references.Add(TestBase.SystemXmlRef);
                references.Add(TestBase.SystemXmlLinqRef);
            }
        }

        var commonReferencesWithoutValueTupleAttribute = element.Attribute(CommonReferencesWithoutValueTupleAttributeName);
        if (commonReferencesWithoutValueTupleAttribute != null &&
            ((bool?)commonReferencesWithoutValueTupleAttribute).HasValue &&
            ((bool?)commonReferencesWithoutValueTupleAttribute).Value)
        {
            references = [TestBase.MscorlibRef_v46, TestBase.SystemRef_v46, TestBase.SystemCoreRef_v46];
        }

        var winRT = element.Attribute(CommonReferencesWinRTAttributeName);
        if (winRT != null &&
            ((bool?)winRT).HasValue &&
            ((bool?)winRT).Value)
        {
            references = [.. TestBase.WinRtRefs];
            if (GetLanguage(element) == LanguageNames.VisualBasic)
            {
                references.Add(TestBase.MsvbRef_v4_0_30319_17929);
                references.Add(TestBase.SystemXmlRef);
                references.Add(TestBase.SystemXmlLinqRef);
            }
        }

        var portable = element.Attribute(CommonReferencesPortableAttributeName);
        if (portable != null &&
            ((bool?)portable).HasValue &&
            ((bool?)portable).Value)
        {
            references = [.. TestBase.PortableRefsMinimal];
        }

        var netcore30 = element.Attribute(CommonReferencesNetCoreAppName);
        if (netcore30 != null &&
            ((bool?)netcore30).HasValue &&
            ((bool?)netcore30).Value)
        {
            references = [.. NetCoreApp.References];
        }

        var netstandard20 = element.Attribute(CommonReferencesNetStandard20Name);
        if (netstandard20 != null &&
            ((bool?)netstandard20).HasValue &&
            ((bool?)netstandard20).Value)
        {
            references = [.. TargetFrameworkUtil.NetStandard20References];
        }

        var net6 = element.Attribute(CommonReferencesNet6Name);
        if (net6 != null &&
            ((bool?)net6).HasValue &&
            ((bool?)net6).Value)
        {
            references = [.. TargetFrameworkUtil.GetReferences(TargetFramework.Net60)];
        }

        var net7 = element.Attribute(CommonReferencesNet7Name);
        if (net7 != null &&
            ((bool?)net7).HasValue &&
            ((bool?)net7).Value)
        {
            references = [.. TargetFrameworkUtil.GetReferences(TargetFramework.Net70)];
        }

        var net8 = element.Attribute(CommonReferencesNet8Name);
        if (net8 != null &&
            ((bool?)net8).HasValue &&
            ((bool?)net8).Value)
        {
            references = [.. TargetFrameworkUtil.GetReferences(TargetFramework.Net80)];
        }

        var net9 = element.Attribute(CommonReferencesNet9Name);
        if (net9 != null &&
            ((bool?)net9).HasValue &&
            ((bool?)net9).Value)
        {
            references = [.. TargetFrameworkUtil.GetReferences(TargetFramework.Net90)];
        }

        var mincorlib = element.Attribute(CommonReferencesMinCorlibName);
        if (mincorlib != null &&
            ((bool?)mincorlib).HasValue &&
            ((bool?)mincorlib).Value)
        {
            references = [TestBase.MinCorlibRef];
        }

        return references;
    }

    public static bool IsWorkspaceElement(string text)
        => text.TrimStart('\r', '\n', ' ').StartsWith("<Workspace>", StringComparison.Ordinal);

    private static void AssertNoChildText(XElement element)
    {
        foreach (var node in element.Nodes())
        {
            if (node is XText text && !string.IsNullOrWhiteSpace(text.Value))
            {
                throw new Exception($"Element {element} has child text that isn't recognized. The XML syntax is invalid.");
            }
        }
    }
}
