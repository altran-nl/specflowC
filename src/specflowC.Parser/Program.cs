﻿using specflowC.Parser.Nodes;
using specflowC.Parser.Output;
using specflowC.Parser.Output.Helpers;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using TechTalk.SpecFlow.Bindings;
using TechTalk.SpecFlow.BindingSkeletons;

namespace specflowC.Parser
{
    internal class Program
    {
        private const string CS_PROJ = @"..\specflowC.IntelliSense.StepDefinitions\specflowC.IntelliSense.StepDefinitions.csproj";
        private const string CPP_PROJ = @"..\Cpp.UnitTest\Cpp.UnitTest.vcxproj";
        private const string PROJECT_NAME = @"Cpp.UnitTest";
        private const string FEATURE_DIR = @"..\" + PROJECT_NAME + "\\";

        private static bool _isDirtyCsProj = false;
        private static bool _isDirtyCppProj = false;
        private static Microsoft.Build.Evaluation.Project _csProj;
        private static Microsoft.Build.Evaluation.Project _cppProj;

        private static bool _singleFile = false;
        private static string _filePath;

        private static void Main(string[] args)
        {
            if (ValidCommandLine(args))
            {
                Console.WriteLine(string.Format("FEATURE FILE: {0}", _filePath));
                string basePath = string.Format("{0}\\", Path.GetDirectoryName(_filePath));
                GenerateTest(_filePath, basePath);
            }
        }

        static private bool ValidCommandLine(string[] args)
        {
            if (args.Length < 1)
            {
                PrintHelp("no args");
                return false;
            }

            if (args[0].ToLower() == "-single" && args.Length == 2)
            {
                _filePath = args[1];
                _singleFile = true;
            }
            else
            {
                _filePath = args[0];
                _singleFile = false;
            }

            if (!File.Exists(_filePath))
            {
                PrintHelp("missing file");
                return false;
            }

            return true;
        }

        static private void PrintHelp(string msg)
        {
            Console.WriteLine(string.Format("Invalid command-line argument. Please specify a feature file: {0}", msg));
        }

        static private void GenerateTest(string featureFilePath, string outputDirectory)
        {
            InputGenerator input = new InputGenerator();
            var features = input.Load(File.ReadAllLines(featureFilePath));

            WriteFile(new HeaderGenerator(), features, outputDirectory, ".h", "UNIT TEST HEADER");
            WriteFile(new CodeBehindGenerator(), features, outputDirectory, "_scenarios.cpp", "SCENARIOS CPP");

            if (!_singleFile)
            {
                AddStepDefinitonToIntelliSenseProject(features, featureFilePath, CS_PROJ);
            }
            WriteFileStepDefinition(features, outputDirectory);

            if (!_singleFile)
            {
                AddFeatureFileLinkToIntelliSenseProject(featureFilePath, FEATURE_DIR, CS_PROJ);

                if (_isDirtyCppProj)
                {
                    _cppProj.Save();
                }
                if (_isDirtyCsProj)
                {
                    _csProj.Save();
                }
            }
        }

        static private UnitTestLanguageConfig GetLanguageConfig()
        {
            return new MSCppUnitTestLanguageConfig();
        }

        static private void WriteFile(IGenerate generator, List<NodeFeature> features, string outputDirectory, string extension, string consoleMessage)
        {
            List<string[]> listOfContents = generator.Generate(GetLanguageConfig(), features);
            for (int i = 0; i < listOfContents.Count; i++)
            {
                string file = string.Format("{0}{1}{2}", outputDirectory, features[i].Name, extension);
                Console.WriteLine(string.Format("{0}: {1}", consoleMessage, file));
                File.WriteAllLines(file, listOfContents[i]);
                if (!_singleFile)
                {
                    AddFilesToCppProject(file, FEATURE_DIR, CPP_PROJ);
                }
            }
        }

        static private void WriteFileStepDefinition(List<NodeFeature> features, string outputDirectory)
        {
            for (int i = 0; i < features.Count; i++)
            {
                string file = outputDirectory + features[i].Name + "_stepDefinitions.cpp";
                Console.WriteLine(string.Format("STEP DEFINITIONS CPP: {0}", file));

                UnitTestLanguageConfig langConfig = GetLanguageConfig();
                if (File.Exists(file))
                {
                    Console.WriteLine("File exists and may contain user-generated code");
                    RemoveAutoGeneratedStepsThatDuplicateUserSteps(file, features[i]);
                    if (features[i].Scenarios.Sum(s => s.Steps.Count) > 0)
                    {
                        Console.WriteLine("Appending new steps");
                        langConfig.UseInclude = false;
                        var listOfContents = new StepDefinitionGenerator().Generate(langConfig, new List<NodeFeature>() { features[i] });
                        if (listOfContents.Count > 0)
                        {
                            File.AppendAllLines(file, listOfContents[0]);
                            if (!_singleFile)
                            {
                                AddFilesToCppProject(file, FEATURE_DIR, CPP_PROJ);
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine("No new steps to append");
                    }
                }
                else
                {
                    var listOfContents = new StepDefinitionGenerator().Generate(langConfig, new List<NodeFeature>() { features[i] });
                    if (listOfContents.Count > 0)
                    {
                        File.WriteAllLines(file, listOfContents[0]);
                        if (!_singleFile)
                        {
                            AddFilesToCppProject(file, FEATURE_DIR, CPP_PROJ);
                        }
                    }
                }
            }
        }

        private static void RemoveAutoGeneratedStepsThatDuplicateUserSteps(string file, NodeFeature feature)
        {
            string[] contents = File.ReadAllLines(file);
            StepDefinitionParser parser = new StepDefinitionParser();
            List<FeatureGroup> groups = parser.Parse(contents);
            var filterGroup = groups.FirstOrDefault(featureGroup => featureGroup.FeatureName == feature.Name);
            if (filterGroup != null)
            {
                foreach (var scenario in feature.Scenarios)
                {
                    foreach (var filterStep in filterGroup.Steps)
                    {
                        scenario.Steps.RemoveAll(step => step.Equals(filterStep));
                    }
                }
            }
        }

        private static void AddStepDefinitonToIntelliSenseProject(List<NodeFeature> features, string pathToFeatureFile, string pathToIntelliSenseProject)
        {
            foreach (var feature in features)
            {
                List<StepInstance> si = new List<StepInstance>();
                var steps = new List<NodeStep>();
                feature.Scenarios.ForEach(s => steps.AddRange(s.Steps));
                var uniqueSteps = GeneratorHelper.FindUniqueSteps(new List<NodeStep>(), steps);

                foreach (var step in uniqueSteps)
                {
                    StepDefinitionType type;
                    StepDefinitionKeyword keyword;
                    string stepNameWithoutType;

                    if (step.Name.StartsWith("Given"))
                    {
                        type = StepDefinitionType.Given;
                        keyword = StepDefinitionKeyword.Given;
                        stepNameWithoutType = step.Name.Substring("Given".Length);
                    }
                    else if (step.Name.StartsWith("When"))
                    {
                        type = StepDefinitionType.When;
                        keyword = StepDefinitionKeyword.When;
                        stepNameWithoutType = step.Name.Substring("When".Length);
                    }
                    else
                    {
                        type = StepDefinitionType.Then;
                        keyword = StepDefinitionKeyword.Then;
                        stepNameWithoutType = step.Name.Substring("Then".Length);
                    }
                    string scenarioName = feature.Scenarios.First(scenario => scenario.Steps.Contains(step)).Name;
                    si.Add(new StepInstance(type, keyword, stepNameWithoutType, stepNameWithoutType, new StepContext(feature.Name, scenarioName, new List<string>(), CultureInfo.CurrentCulture)));
                }

                var stepDefSkeleton = new StepDefinitionSkeletonProvider(new SpecFlowCSkeletonTemplateProvider(), new StepTextAnalyzer());
                var template = stepDefSkeleton.GetBindingClassSkeleton(TechTalk.SpecFlow.ProgrammingLanguage.CSharp, si.ToArray(), "CppUnitTest", feature.Name, StepDefinitionSkeletonStyle.MethodNamePascalCase, CultureInfo.CurrentCulture);

                string basePathToFeatures = Path.GetDirectoryName(pathToFeatureFile);
                string basePathToIntelliSenseProject = Path.GetDirectoryName(pathToIntelliSenseProject);
                _csProj = _csProj ?? GetUnloadedProject(pathToIntelliSenseProject);

                var stepDefinitionDirPathInProj = string.Format("Steps\\{0}\\", PROJECT_NAME);
                var stepDefinitionDirPath = string.Format("{0}\\{1}", basePathToIntelliSenseProject, stepDefinitionDirPathInProj);

                var filePathInProjFile = string.Format("{0}{1}_step.cs", stepDefinitionDirPathInProj, feature.Name);
                var filePath = string.Format("{0}{1}_step.cs", stepDefinitionDirPath, feature.Name);

                if (!_csProj.GetItems("Compile").Any(item => item.UnevaluatedInclude == filePathInProjFile))
                {
                    Console.WriteLine(string.Format("Generating Step Definition file for IntelliSense support: {0}", filePathInProjFile));
                    Directory.CreateDirectory(stepDefinitionDirPath);
                    File.WriteAllText(filePath, template);
                    _csProj.AddItem("Compile", filePathInProjFile);
                    _isDirtyCsProj = true;
                }
            }
        }

        private static Microsoft.Build.Evaluation.Project GetUnloadedProject(string projectPath)
        {
            var project = new Microsoft.Build.Evaluation.Project(projectPath);
            project.ProjectCollection.UnloadAllProjects();
            return project;
        }

        private static void AddFilesToCppProject(string pathToFile, string featureDir, string pathToCppProject)
        {
            _cppProj = _cppProj ?? GetUnloadedProject(pathToCppProject);

            pathToFile = MakeFeatureDirRelativeToCppProject(pathToFile, featureDir);

            string type = CppFileType(pathToFile);

            if (!_cppProj.GetItems(type).Any(item => item.UnevaluatedInclude == pathToFile))
            {
                _cppProj.AddItem(type, pathToFile);
                _isDirtyCppProj = true;
            }
        }

        private static string MakeFeatureDirRelativeToCppProject(string pathToFile, string featureDir)
        {
            // could be called from CPP build (already relative) or CS build (make relative to CPP in this case)
            if (pathToFile.StartsWith(featureDir))
            {
                pathToFile = pathToFile.Substring(pathToFile.IndexOf(featureDir) + featureDir.Length);
            }
            return pathToFile;
        }

        private static string CppFileType(string pathToFile)
        {
            string type;
            if (pathToFile.Contains(".h"))
            {
                type = "ClInclude";
            }
            else
            {
                type = "ClCompile";
            }
            return type;
        }

        private static void AddFeatureFileLinkToIntelliSenseProject(string featureFilePath, string featureDir, string pathToIntelliSenseProject)
        {
            _csProj = _csProj ?? GetUnloadedProject(pathToIntelliSenseProject);
            featureFilePath = MakeLinkRelativeToIntelliSenseProject(featureFilePath, featureDir);
            var featureFileLink = featureFilePath.Replace(@"..\", string.Empty);
            if (!_csProj.Items.Any(item => item.GetMetadataValue("Link") == featureFileLink))
            {
                _csProj.AddItem("None", featureFilePath, new Dictionary<string, string> { { "Link", featureFileLink } });
                _isDirtyCsProj = true;
            }
        }

        private static string MakeLinkRelativeToIntelliSenseProject(string featureFilePath, string featureDir)
        {
            // could be called from CPP build (make relative to CS in this case) or CS build (already relative)
            if (!featureFilePath.StartsWith(featureDir))
            {
                featureFilePath = featureDir + featureFilePath;
            }
            return featureFilePath;
        }
    }
}