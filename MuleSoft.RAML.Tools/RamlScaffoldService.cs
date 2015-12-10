﻿using EnvDTE;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using MuleSoft.RAML.Tools.Properties;
using NuGet.VisualStudio;
using Raml.Common;
using Raml.Tools;
using Raml.Tools.WebApiGenerator;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;

namespace MuleSoft.RAML.Tools
{
    public class RamlScaffoldService
    {
        private const string RamlSpecVersion = "0.8";
        private const string ControllerBaseTemplateName = "ApiControllerBase.t4";
        private const string ControllerInterfaceTemplateName = "ApiControllerInterface.t4";
        private const string ControllerImplementationTemplateName = "ApiControllerImplementation.t4";
        private const string ModelTemplateName = "ApiModel.t4";
        private const string EnumTemplateName = "ApiEnum.t4";

        private readonly string ContractsFolderName = Settings.Default.ContractsFolderName;
        private readonly IT4Service t4Service;
        private readonly IServiceProvider serviceProvider;
        private readonly TemplatesManager templatesManager = new TemplatesManager();
        private static readonly string ContractsFolder = Path.DirectorySeparatorChar + Settings.Default.ContractsFolderName + Path.DirectorySeparatorChar;
        private static readonly string IncludesFolder = Path.DirectorySeparatorChar + "includes" + Path.DirectorySeparatorChar;

        private readonly string nugetPackagesSource = Settings.Default.NugetPackagesSource;
        private readonly string ramlApiCorePackageId = Settings.Default.RAMLApiCorePackageId;
        private readonly string ramlApiCorePackageVersion = Settings.Default.RAMLApiCorePackageVersion;
        private readonly string newtonsoftJsonPackageId = Settings.Default.NewtonsoftJsonPackageId;
        private readonly string newtonsoftJsonPackageVersion = Settings.Default.NewtonsoftJsonPackageVersion;
        private readonly string microsoftNetHttpPackageId = Settings.Default.MicrosoftNetHttpPackageId;
        private readonly string microsoftNetHttpPackageVersion = Settings.Default.MicrosoftNetHttpPackageVersion;
        private string templateSubFolder;

        public RamlScaffoldService(IT4Service t4Service, IServiceProvider serviceProvider)
        {
            this.t4Service = t4Service;
            this.serviceProvider = serviceProvider;
        }

        public void AddContract(RamlChooserActionParams parameters)
        {
            var dte = serviceProvider.GetService(typeof(SDTE)) as DTE;
            var proj = VisualStudioAutomationHelper.GetActiveProject(dte);

            InstallNugetDependencies(proj);
            AddXmlFormatterInWebApiConfig(proj);

            if (VisualStudioAutomationHelper.IsAVisualStudio2015Project(proj))
                templateSubFolder = "AspNet5";
            else
                templateSubFolder = "RAMLWebApi2Scaffolder";

            var folderItem = VisualStudioAutomationHelper.AddFolderIfNotExists(proj, ContractsFolderName);
            var generatedFolderPath = Path.GetDirectoryName(proj.FullName) + Path.DirectorySeparatorChar + ContractsFolderName + Path.DirectorySeparatorChar;

            if (string.IsNullOrWhiteSpace(parameters.RamlSource) && !string.IsNullOrWhiteSpace(parameters.RamlTitle))
            {
                AddEmptyContract(parameters.TargetFileName, parameters.RamlTitle, folderItem, generatedFolderPath,
                    parameters.TargetNamespace, parameters.TargetFileName, parameters.UseAsyncMethods);
            }
            else
            {
                AddContractFromFile(parameters.RamlFilePath, parameters.TargetNamespace, parameters.RamlSource, folderItem,
                    generatedFolderPath, parameters.TargetFileName, parameters.UseAsyncMethods);
            }
        }


        public void Scaffold(string ramlSource, string targetNamespace, string ramlFileName, bool useAsyncMethods)
        {
            var data = RamlScaffolderHelper.GetRamlData(ramlSource, targetNamespace);
            if (data == null || data.Model == null)
                return;

            var model = data.Model;

            var dte = serviceProvider.GetService(typeof(SDTE)) as DTE;
            var proj = VisualStudioAutomationHelper.GetActiveProject(dte);

            if (VisualStudioAutomationHelper.IsAVisualStudio2015Project(proj))
                templateSubFolder = "AspNet5";
            else
                templateSubFolder = "RAMLWebApi2Scaffolder";

            var folderItem = VisualStudioAutomationHelper.AddFolderIfNotExists(proj, ContractsFolderName);
            var ramlItem = folderItem.ProjectItems.Cast<ProjectItem>().First(i => i.Name.ToLowerInvariant() == ramlFileName.ToLowerInvariant());
            var generatedFolderPath = Path.GetDirectoryName(proj.FullName) + Path.DirectorySeparatorChar + ContractsFolderName + Path.DirectorySeparatorChar;

            if (!templatesManager.ConfirmWhenIncompatibleServerTemplate(generatedFolderPath,
                new[] { ControllerBaseTemplateName, ControllerInterfaceTemplateName, ControllerImplementationTemplateName, ModelTemplateName, EnumTemplateName }))
                return;

            var extensionPath = Path.GetDirectoryName(GetType().Assembly.Location) + Path.DirectorySeparatorChar;
            
            AddOrUpdateModels(targetNamespace, generatedFolderPath, ramlItem, model, folderItem, extensionPath);

            AddOrUpdateEnums(targetNamespace, generatedFolderPath, ramlItem, model, folderItem, extensionPath);

            AddOrUpdateControllerBase(targetNamespace, generatedFolderPath, ramlItem, model, folderItem, extensionPath, useAsyncMethods);

            AddOrUpdateControllerInterfaces(targetNamespace, generatedFolderPath, ramlItem, model, folderItem, extensionPath, useAsyncMethods);

            AddOrUpdateControllerImplementations(targetNamespace, generatedFolderPath, proj, model, folderItem, extensionPath, useAsyncMethods);
        }

        public static void TriggerScaffoldOnRamlChanged(Document document)
        {
            if (!IsInContractsFolder(document)) 
                return;

            ScaffoldMainRamlFiles(GetMainRamlFiles(document));
        }

        private void InstallNugetDependencies(Project proj)
        {
            // RAML.Api.Core
            var componentModel = (IComponentModel)serviceProvider.GetService(typeof(SComponentModel));
            var installerServices = componentModel.GetService<IVsPackageInstallerServices>();
            var installer = componentModel.GetService<IVsPackageInstaller>();

            var packs = installerServices.GetInstalledPackages(proj).ToArray();
            NugetInstallerHelper.InstallPackageIfNeeded(proj, packs, installer, newtonsoftJsonPackageId, newtonsoftJsonPackageVersion);
            NugetInstallerHelper.InstallPackageIfNeeded(proj, packs, installer, microsoftNetHttpPackageId, microsoftNetHttpPackageVersion);

            // System.Xml.XmlSerializer 4.0.11-beta-23516
            // NugetInstallerHelper.InstallPackageIfNeeded(proj, packs, installer, "System.Xml.XmlSerializer", "4.0.11-beta-23516");

            // RAML.Api.Core
            if (!installerServices.IsPackageInstalled(proj, ramlApiCorePackageId))
            {
                installer.InstallPackage(nugetPackagesSource, proj, ramlApiCorePackageId, ramlApiCorePackageVersion, false);
            }
        }

        private static void AddXmlFormatterInWebApiConfig(Project proj)
        {
            var appStart = proj.ProjectItems.Cast<ProjectItem>().FirstOrDefault(i => i.Name == "App_Start");
            if (appStart == null) return;

            var webApiConfig = appStart.ProjectItems.Cast<ProjectItem>().FirstOrDefault(i => i.Name == "WebApiConfig.cs");
            if (webApiConfig == null) return;

            var path = webApiConfig.FileNames[0];
            var lines = File.ReadAllLines(path).ToList();

            if (lines.Any(l => l.Contains("XmlSerializerFormatter")))
                return;

            InsertLine(lines);

            File.WriteAllText(path, string.Join(Environment.NewLine, lines));
        }

        private static void InsertInStartup(List<string> lines)
        {
            var line = FindLineWith(lines, "Register(HttpConfiguration config)");
            var inserted = false;

            if (line != -1)
            {
                if (lines[line + 1].Contains("{"))
                {
                    InsertLines(lines, line + 2);
                    inserted = true;
                }
            }

            if (inserted) return;

            line = FindLineWith(lines, ".MapHttpAttributeRoutes();");
            if (line != -1)
            {
                InsertLines(lines, line + 1);
            }
        }

        private static void InsertLine(List<string> lines)
        {
            var line = FindLineWith(lines, "Register(HttpConfiguration config)");
            var inserted = false;

            if (line != -1)
            {
                if (lines[line + 1].Contains("{"))
                {
                    InsertLines(lines, line + 2);
                    inserted = true;
                }
            }

            if (inserted) return;

            line = FindLineWith(lines, ".MapHttpAttributeRoutes();");
            if (line != -1)
            {
                InsertLines(lines, line + 1);
            }
        }

        private static void InsertLines(IList<string> lines, int index)
        {
            lines.Insert(index, "\t\t\tconfig.Formatters.Remove(config.Formatters.XmlFormatter);");
            lines.Insert(index, "\t\t\tconfig.Formatters.Add(new RAML.Api.Core.XmlSerializerFormatter());");
        }

        private static int FindLineWith(IReadOnlyList<string> lines, string find)
        {
            var line = -1;
            for (var i = 0; i < lines.Count(); i++)
            {
                if (lines[i].Contains(find))
                    line = i;
            }
            return line;
        }

        private static void ScaffoldMainRamlFiles(IEnumerable<string> ramlFiles)
        {
            var globalProvider = ServiceProvider.GlobalProvider;
            var service = new RamlScaffoldService(new T4Service(globalProvider), ServiceProvider.GlobalProvider);
            foreach (var ramlFile in ramlFiles)
            {
                var refFilePath = InstallerServices.GetRefFilePath(ramlFile);
                var useAsyncMethods = RamlReferenceReader.GetRamlUseAsyncMethods(refFilePath);
                var targetNamespace = RamlReferenceReader.GetRamlNamespace(refFilePath);
                service.Scaffold(ramlFile, targetNamespace, Path.GetFileName(ramlFile), useAsyncMethods);
            }
        }

        private static IEnumerable<string> GetMainRamlFiles(Document document)
        {
            var path = document.Path.ToLowerInvariant();

            if (IsMainRamlFile(document, path))
                return new [] {document.FullName};

            var ramlItems = GetMainRamlFileFromProject();
            return GetItemsWithReferenceFiles(ramlItems);
        }

        private static bool IsMainRamlFile(Document document, string path)
        {
            return !path.EndsWith(IncludesFolder) && document.Name.ToLowerInvariant().EndsWith(".raml") && HasReferenceFile(document.FullName);
        }

        private static IEnumerable<string> GetItemsWithReferenceFiles(IEnumerable<ProjectItem> ramlItems)
        {
            var items = new List<string>();
            foreach (var item in ramlItems)
            {
                if (HasReferenceFile(item.FileNames[0]))
                    items.Add(item.FileNames[0]);
            }
            return items;
        }

        private static bool HasReferenceFile(string ramlFilePath)
        {
            var refFilePath = InstallerServices.GetRefFilePath(ramlFilePath);
            var hasReferenceFile = !string.IsNullOrWhiteSpace(refFilePath) && File.Exists(refFilePath);
            return hasReferenceFile;
        }

        private static IEnumerable<ProjectItem> GetMainRamlFileFromProject()
        {
            var dte = ServiceProvider.GlobalProvider.GetService(typeof(SDTE)) as DTE;
            var proj = VisualStudioAutomationHelper.GetActiveProject(dte);
            var contractsItem =
                proj.ProjectItems.Cast<ProjectItem>().FirstOrDefault(i => i.Name == Settings.Default.ContractsFolderName);

            if (contractsItem == null)
                throw new InvalidOperationException("Could not find main RAML file");

            var ramlItems = contractsItem.ProjectItems.Cast<ProjectItem>().Where(i => i.Name.EndsWith(".raml")).ToArray();
            if (!ramlItems.Any())
                throw new InvalidOperationException("Could not find main RAML file");

            return ramlItems;
        }

        private static bool IsInContractsFolder(Document document)
        {
            return document.Path.ToLowerInvariant().Contains(ContractsFolder.ToLowerInvariant());
        }

        private void AddOrUpdateControllerImplementations(string targetNamespace, string generatedFolderPath, Project proj,
            WebApiGeneratorModel model, ProjectItem folderItem, string extensionPath, bool useAsyncMethods)
        {
            templatesManager.CopyServerTemplateToProjectFolder(generatedFolderPath, ControllerImplementationTemplateName,
                Settings.Default.ControllerImplementationTemplateTitle, templateSubFolder);
            var controllersFolderItem = VisualStudioAutomationHelper.AddFolderIfNotExists(proj, "Controllers");
            var controllersFolderPath = Path.GetDirectoryName(proj.FullName) + Path.DirectorySeparatorChar + "Controllers" +
                                        Path.DirectorySeparatorChar;
            var templatesFolder = Path.Combine(generatedFolderPath, "Templates");
            var controllerImplementationTemplateParams =
                new TemplateParams<ControllerObject>(Path.Combine(templatesFolder, ControllerImplementationTemplateName),
                    controllersFolderItem, "controllerObject", model.Controllers, controllersFolderPath, folderItem,
                    extensionPath, targetNamespace, "Controller", false);
            controllerImplementationTemplateParams.Title = Settings.Default.ControllerImplementationTemplateTitle;
            controllerImplementationTemplateParams.IncludeHasModels = true;
            controllerImplementationTemplateParams.HasModels = model.Objects.Any() || model.Enums.Any();
            controllerImplementationTemplateParams.UseAsyncMethods = useAsyncMethods;
            GenerateCodeFromTemplate(controllerImplementationTemplateParams);
        }

        private void AddOrUpdateControllerInterfaces(string targetNamespace, string generatedFolderPath, ProjectItem ramlItem,
            WebApiGeneratorModel model, ProjectItem folderItem, string extensionPath, bool useAsyncMethods)
        {
            templatesManager.CopyServerTemplateToProjectFolder(generatedFolderPath, ControllerInterfaceTemplateName,
                Settings.Default.ControllerInterfaceTemplateTitle, templateSubFolder);
            var templatesFolder = Path.Combine(generatedFolderPath, "Templates");
            var controllerInterfaceParams =
                new TemplateParams<ControllerObject>(Path.Combine(templatesFolder, ControllerInterfaceTemplateName),
                    ramlItem, "controllerObject", model.Controllers, generatedFolderPath, folderItem, extensionPath,
                    targetNamespace, "Controller", true, "I");
            controllerInterfaceParams.Title = Settings.Default.ControllerInterfaceTemplateTitle;
            controllerInterfaceParams.IncludeHasModels = true;
            controllerInterfaceParams.HasModels = model.Objects.Any() || model.Enums.Any();
            controllerInterfaceParams.UseAsyncMethods = useAsyncMethods;
            GenerateCodeFromTemplate(controllerInterfaceParams);
        }

        private void AddOrUpdateControllerBase(string targetNamespace, string generatedFolderPath, ProjectItem ramlItem,
            WebApiGeneratorModel model, ProjectItem folderItem, string extensionPath, bool useAsyncMethods)
        {
            templatesManager.CopyServerTemplateToProjectFolder(generatedFolderPath, ControllerBaseTemplateName,
                Settings.Default.BaseControllerTemplateTitle, templateSubFolder);
            var templatesFolder = Path.Combine(generatedFolderPath, "Templates");
            var controllerBaseTemplateParams =
                new TemplateParams<ControllerObject>(Path.Combine(templatesFolder, ControllerBaseTemplateName),
                    ramlItem, "controllerObject", model.Controllers, generatedFolderPath, folderItem, extensionPath,
                    targetNamespace, "Controller");
            controllerBaseTemplateParams.Title = Settings.Default.BaseControllerTemplateTitle;
            controllerBaseTemplateParams.IncludeHasModels = true;
            controllerBaseTemplateParams.HasModels = model.Objects.Any() || model.Enums.Any();
            controllerBaseTemplateParams.UseAsyncMethods = useAsyncMethods;
            GenerateCodeFromTemplate(controllerBaseTemplateParams);
        }

        private void AddOrUpdateModels(string targetNamespace, string generatedFolderPath, ProjectItem ramlItem,
            WebApiGeneratorModel model, ProjectItem folderItem, string extensionPath)
        {
            templatesManager.CopyServerTemplateToProjectFolder(generatedFolderPath, ModelTemplateName,
                Settings.Default.ModelsTemplateTitle, templateSubFolder);
            var templatesFolder = Path.Combine(generatedFolderPath, "Templates");
            
            var models = model.Objects;
            // when is an XML model, skip empty objects
            if (model.Objects.Any(o => !string.IsNullOrWhiteSpace(o.GeneratedCode)))
                models = model.Objects.Where(o => o.Properties.Any() || !string.IsNullOrWhiteSpace(o.GeneratedCode));

            models = models.Where(o => !o.IsArray || o.Type == null); // skip array of primitives

            var apiObjectTemplateParams = new TemplateParams<ApiObject>(
                Path.Combine(templatesFolder, ModelTemplateName), ramlItem, "apiObject", models,
                generatedFolderPath, folderItem, extensionPath, targetNamespace);
            apiObjectTemplateParams.Title = Settings.Default.ModelsTemplateTitle;
            GenerateCodeFromTemplate(apiObjectTemplateParams);
        }

        private void AddOrUpdateEnums(string targetNamespace, string generatedFolderPath, ProjectItem ramlItem,
            WebApiGeneratorModel model, ProjectItem folderItem, string extensionPath)
        {
            templatesManager.CopyServerTemplateToProjectFolder(generatedFolderPath, EnumTemplateName,
                Settings.Default.EnumsTemplateTitle, templateSubFolder);
            var templatesFolder = Path.Combine(generatedFolderPath, "Templates");
            var apiEnumTemplateParams = new TemplateParams<ApiEnum>(
                Path.Combine(templatesFolder, EnumTemplateName), ramlItem, "apiEnum", model.Enums,
                generatedFolderPath, folderItem, extensionPath, targetNamespace);
            apiEnumTemplateParams.Title = Settings.Default.ModelsTemplateTitle;
            GenerateCodeFromTemplate(apiEnumTemplateParams);
        }


        public void UpdateRaml(string ramlFilePath)
        {
            var dte = serviceProvider.GetService(typeof(SDTE)) as DTE;
            var proj = VisualStudioAutomationHelper.GetActiveProject(dte);
            var generatedFolderPath = Path.GetDirectoryName(proj.FullName) + Path.DirectorySeparatorChar + ContractsFolderName + Path.DirectorySeparatorChar;

            var refFilePath = InstallerServices.GetRefFilePath(ramlFilePath);
            var includesFolderPath = generatedFolderPath + Path.DirectorySeparatorChar + InstallerServices.IncludesFolderName;
            var ramlSource = RamlReferenceReader.GetRamlSource(refFilePath);
            var includesManager = new RamlIncludesManager();
            var result = includesManager.Manage(ramlSource, includesFolderPath, generatedFolderPath + Path.DirectorySeparatorChar);
            if (result.IsSuccess)
            {
                File.WriteAllText(ramlFilePath, result.ModifiedContents);
                var targetNamespace = RamlReferenceReader.GetRamlNamespace(refFilePath);
                var useAsyncMethods = RamlReferenceReader.GetRamlUseAsyncMethods(refFilePath);
                Scaffold(ramlFilePath, targetNamespace, Path.GetFileName(ramlFilePath).ToLowerInvariant(), useAsyncMethods);
            }
        }

        private void AddContractFromFile(string ramlFilePath, string targetNamespace, string ramlSource, ProjectItem folderItem, string folderPath, string targetFilename, bool useAsyncMethod)
        {
            var includesFolderPath = folderPath + Path.DirectorySeparatorChar + InstallerServices.IncludesFolderName;

            var includesManager = new RamlIncludesManager();
            var result = includesManager.Manage(ramlSource, includesFolderPath, confirmOverrite: true, rootRamlPath: folderPath + Path.DirectorySeparatorChar);

            var includesFolderItem = folderItem.ProjectItems.Cast<ProjectItem>().FirstOrDefault(i => i.Name == InstallerServices.IncludesFolderName);
            if (includesFolderItem == null)
                includesFolderItem = folderItem.ProjectItems.AddFolder(InstallerServices.IncludesFolderName);

            foreach (var file in result.IncludedFiles)
            {
                includesFolderItem.ProjectItems.AddFromFile(file);
            }

            //var existingIncludeItems = includesFolderItem.ProjectItems.Cast<ProjectItem>();
            //var oldIncludedFiles = existingIncludeItems.Where(item => !result.IncludedFiles.Contains(item.FileNames[0]));
            //InstallerServices.RemoveSubItemsAndAssociatedFiles(oldIncludedFiles);

            var ramlProjItem = AddOrUpdateRamlFile(result.ModifiedContents, folderItem, folderPath, targetFilename);
            InstallerServices.RemoveSubItemsAndAssociatedFiles(ramlProjItem);

            var refFilePath = InstallerServices.AddRefFile(ramlFilePath, targetNamespace, ramlSource, folderPath, targetFilename, useAsyncMethod);
            ramlProjItem.ProjectItems.AddFromFile(refFilePath);

            Scaffold(ramlProjItem.FileNames[0], targetNamespace, targetFilename, useAsyncMethod);
        }

        private static ProjectItem AddOrUpdateRamlFile(string modifiedContents, ProjectItem folderItem, string folderPath, string ramlFileName)
        {
            ProjectItem ramlProjItem;
            var ramlDestFile = Path.Combine(folderPath, ramlFileName);

            if (File.Exists(ramlDestFile))
            {
                var dialogResult = InstallerServices.ShowConfirmationDialog(ramlFileName);

                if (dialogResult == MessageBoxResult.Yes)
                {
                    File.WriteAllText(ramlDestFile, modifiedContents);
                    ramlProjItem = folderItem.ProjectItems.AddFromFile(ramlDestFile);
                }
                else
                {
                    ramlProjItem = folderItem.ProjectItems.Cast<ProjectItem>().FirstOrDefault(i => i.Name == ramlFileName);
                    if (ramlProjItem == null)
                        ramlProjItem = folderItem.ProjectItems.AddFromFile(ramlDestFile);
                }
            }
            else
            {
                File.WriteAllText(ramlDestFile, modifiedContents);
                ramlProjItem = folderItem.ProjectItems.AddFromFile(ramlDestFile);
            }
            return ramlProjItem;
        }

        private void AddEmptyContract(string filename, string title, ProjectItem folderItem, string folderPath, string targetNamespace, string targetFilename, bool useAsyncMethods)
        {
            
            var newContractFile = Path.Combine(folderPath, filename);
            var contents = CreateNewRamlContents(title);

            ProjectItem ramlProjItem;
            if (File.Exists(newContractFile))
            {
                var dialogResult = InstallerServices.ShowConfirmationDialog(filename);
                if (dialogResult == MessageBoxResult.Yes)
                {
                    File.WriteAllText(newContractFile, contents);
                    ramlProjItem = folderItem.ProjectItems.AddFromFile(newContractFile);
                }
                else
                {
                    ramlProjItem = folderItem.ProjectItems.Cast<ProjectItem>().FirstOrDefault(i => i.Name == newContractFile);
                    if (ramlProjItem == null)
                        ramlProjItem = folderItem.ProjectItems.AddFromFile(newContractFile);
                }
            }
            else
            {
                File.WriteAllText(newContractFile, contents);
                ramlProjItem = folderItem.ProjectItems.AddFromFile(newContractFile);
            }

            var refFilePath = InstallerServices.AddRefFile(newContractFile, targetNamespace, newContractFile, folderPath, targetFilename, useAsyncMethods);
            ramlProjItem.ProjectItems.AddFromFile(refFilePath);
        }

        private static string CreateNewRamlContents(string title)
        {
            var contents = "#%RAML " + RamlSpecVersion + Environment.NewLine +
                           "title: " + title + Environment.NewLine;
            return contents;
        }

        public class TemplateParams<TT> where TT : IHasName
        {
            private string _templatePath;
            private ProjectItem _projItem;
            private string _parameterName;
            private IEnumerable<TT> _parameterCollection;
            private string _folderPath;
            private ProjectItem _folderItem;
            private string _binPath;
            private string _targetNamespace;
            private string _suffix;
            private bool _ovewrite;
            private string _prefix;

            public TemplateParams(string templatePath, ProjectItem projItem, string parameterName, IEnumerable<TT> parameterCollection, string folderPath, ProjectItem folderItem, string binPath, string targetNamespace, string suffix = null, bool ovewrite = true, string prefix = null)
            {
                _templatePath = templatePath;
                _projItem = projItem;
                _parameterName = parameterName;
                _parameterCollection = parameterCollection;
                _folderPath = folderPath;
                _folderItem = folderItem;
                _binPath = binPath;
                _targetNamespace = targetNamespace;
                _suffix = suffix;
                _ovewrite = ovewrite;
                _prefix = prefix;
            }

            public string TemplatePath
            {
                get { return _templatePath; }
            }

            public ProjectItem ProjItem
            {
                get { return _projItem; }
            }

            public string ParameterName
            {
                get { return _parameterName; }
            }

            public IEnumerable<TT> ParameterCollection
            {
                get { return _parameterCollection; }
            }

            public string FolderPath
            {
                get { return _folderPath; }
            }

            public ProjectItem FolderItem
            {
                get { return _folderItem; }
            }

            public string BinPath
            {
                get { return _binPath; }
            }

            public string TargetNamespace
            {
                get { return _targetNamespace; }
            }

            public string Suffix
            {
                get { return _suffix; }
            }

            public bool Ovewrite
            {
                get { return _ovewrite; }
            }

            public string Prefix
            {
                get { return _prefix; }
            }

            public string Title { get; set; }

            public bool IncludeHasModels { get; set; }

            public bool HasModels { get; set; }
            public bool UseAsyncMethods { get; set; }
        }

        private void GenerateCodeFromTemplate<T>(TemplateParams<T> templateParams) where T : IHasName
        {

            foreach (var parameter in templateParams.ParameterCollection)
            {
                var generatedFileName = GetGeneratedFileName(templateParams.Suffix, templateParams.Prefix, parameter);

                var result = t4Service.TransformText(templateParams.TemplatePath, templateParams.ParameterName, parameter, templateParams.BinPath, templateParams.TargetNamespace, templateParams.UseAsyncMethods, templateParams.IncludeHasModels, templateParams.HasModels);
                var destinationFile = Path.Combine(templateParams.FolderPath, generatedFileName);
                var contents = templatesManager.AddServerMetadataHeader(result.Content, Path.GetFileNameWithoutExtension(templateParams.TemplatePath), templateParams.Title);
                
                if(templateParams.Ovewrite || !File.Exists(destinationFile))
                {
                    File.WriteAllText(destinationFile, contents);
                }

                // add file if it does not exist
                var fileItem = templateParams.ProjItem.ProjectItems.Cast<ProjectItem>().FirstOrDefault(i => i.Name == generatedFileName);
                if (fileItem != null) continue;

                if (templateParams.ProjItem.Name.EndsWith(".raml"))
                {
                    var alreadyIncludedInProj = IsAlreadyIncludedInProject(templateParams.FolderPath, templateParams.FolderItem, generatedFileName, templateParams.ProjItem);
                    if (!alreadyIncludedInProj)
                        templateParams.ProjItem.ProjectItems.AddFromFile(destinationFile);
                }
                else
                {
                    templateParams.ProjItem.ProjectItems.AddFromFile(destinationFile);
                }
            }
        }

        private static bool IsAlreadyIncludedInProject(string folderPath, ProjectItem folderItem, string generatedFileName, ProjectItem fileItem)
        {
            if (VisualStudioAutomationHelper.IsAVisualStudio2015Project(fileItem.ContainingProject))
                return File.Exists(Path.Combine(folderPath, generatedFileName));

            var otherRamlFiles = GetOtherRamlFilesInProject(folderPath, fileItem);
            var alreadyIncludedInProj = false;
            foreach (var ramlFile in otherRamlFiles)
            {
                var fileName = Path.GetFileName(ramlFile);
                var otherRamlFileItem =
                    folderItem.ProjectItems.Cast<ProjectItem>().FirstOrDefault(i => i.Name == fileName);

                if (otherRamlFileItem == null) continue;
                var item = otherRamlFileItem.ProjectItems.Cast<ProjectItem>().FirstOrDefault(i => i.Name == generatedFileName);
                alreadyIncludedInProj = alreadyIncludedInProj || (item != null);
            }
            return alreadyIncludedInProj;
        }

        private static IEnumerable<string> GetOtherRamlFilesInProject(string folderPath, ProjectItem fileItem)
        {
            var ramlFiles = Directory.EnumerateFiles(folderPath, "*.raml").ToArray();
            var currentRamlFile = fileItem.FileNames[0];
            var otherRamlFiles =
                ramlFiles.Where(f => !String.Equals(f, currentRamlFile, StringComparison.InvariantCultureIgnoreCase));
            return otherRamlFiles;
        }

        private static string GetGeneratedFileName<T>(string suffix, string prefix, T parameter) where T : IHasName
        {
            var name = parameter.Name;
            if (!string.IsNullOrWhiteSpace(prefix))
                name = prefix + name;
            if (!string.IsNullOrWhiteSpace(suffix))
                name += suffix;

            var generatedFileName = name + ".cs";
            return generatedFileName;
        }


    }
}