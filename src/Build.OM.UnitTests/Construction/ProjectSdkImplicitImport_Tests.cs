// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Exceptions;
using Microsoft.Build.Shared;
using Xunit;

namespace Microsoft.Build.UnitTests.OM.Construction
{
    /// <summary>
    /// Tests for the ProjectImportElement class when imports are implicit through an Sdk specification.
    /// </summary>
    public class ProjectSdkImplicitImport_Tests : IDisposable
    {
        private const string ProjectTemplateSdkAsAttribute = @"
<Project Sdk=""{0}"">
  {1}
</Project>";

        private const string ProjectTemplateSdkAsElement = @"
<Project>
  <Sdk Name=""{0}"" />
  {1}
</Project>";

        private const string ProjectTemplateSdkAsExplicitImport = @"
<Project>
  <Import Project=""Sdk.props"" Sdk=""{0}"" />
  {1}
  <Import Project=""Sdk.targets"" Sdk=""{0}"" />
</Project>";

        private const string SdkName = "MSBuildUnitTestSdk";
        private readonly string _testSdkRoot;
        private readonly string _testSdkDirectory;
        private readonly string _sdkPropsPath;
        private readonly string _sdkTargetsPath;

        public ProjectSdkImplicitImport_Tests()
        {
            _testSdkRoot = Path.Combine(ObjectModelHelpers.TempProjectDir, Guid.NewGuid().ToString("N"));
            _testSdkDirectory = Path.Combine(_testSdkRoot, SdkName, "Sdk");
            _sdkPropsPath = Path.Combine(_testSdkDirectory, "Sdk.props");
            _sdkTargetsPath = Path.Combine(_testSdkDirectory, "Sdk.targets");

            Directory.CreateDirectory(_testSdkDirectory);
        }

        [Theory]
        [InlineData(ProjectTemplateSdkAsAttribute, false)]
        [InlineData(ProjectTemplateSdkAsElement, true)]
        [InlineData(ProjectTemplateSdkAsExplicitImport, false)]
        public void SdkImportsAreInLogicalProject(string projectFormatString, bool expectImportInLogicalProject)
        {
            string projectInnerContents = @"<PropertyGroup><UsedToTestIfImplicitImportsAreInTheCorrectLocation>null</UsedToTestIfImplicitImportsAreInTheCorrectLocation></PropertyGroup>";
            File.WriteAllText(_sdkPropsPath, "<Project><PropertyGroup><InitialImportProperty>Hello</InitialImportProperty></PropertyGroup></Project>");
            File.WriteAllText(_sdkTargetsPath, "<Project><PropertyGroup><FinalImportProperty>World</FinalImportProperty></PropertyGroup></Project>");

            using (new Helpers.TemporaryEnvironment("MSBuildSDKsPath", _testSdkRoot))
            {
                string content = string.Format(projectFormatString, SdkName, projectInnerContents);

                ProjectRootElement projectRootElement = ProjectRootElement.Create(XmlReader.Create(new StringReader(content)));

                var project = new Project(projectRootElement);

                IList<ProjectElement> children = project.GetLogicalProject().ToList();

                // <Sdk> style will have an extra ProjectElment.
                Assert.Equal(expectImportInLogicalProject ? 7 : 6, children.Count);
            }
        }

        [Theory]
        [InlineData(ProjectTemplateSdkAsAttribute, false)]
        [InlineData(ProjectTemplateSdkAsElement, false)]
        [InlineData(ProjectTemplateSdkAsExplicitImport, true)]
        public void SdkImportsAreInImportList(string projectFormatString, bool expectImportInLogicalProject)
        {
            string projectInnerContents = @"<PropertyGroup><UsedToTestIfImplicitImportsAreInTheCorrectLocation>null</UsedToTestIfImplicitImportsAreInTheCorrectLocation></PropertyGroup>";
            File.WriteAllText(_sdkPropsPath, "<Project><PropertyGroup><InitialImportProperty>Hello</InitialImportProperty></PropertyGroup></Project>");
            File.WriteAllText(_sdkTargetsPath, "<Project><PropertyGroup><FinalImportProperty>World</FinalImportProperty></PropertyGroup></Project>");

            using (new Helpers.TemporaryEnvironment("MSBuildSDKsPath", _testSdkRoot))
            {
                string content = string.Format(projectFormatString, SdkName, projectInnerContents);

                ProjectRootElement projectRootElement = ProjectRootElement.Create(XmlReader.Create(new StringReader(content)));

                var project = new Project(projectRootElement);

                // The XML representation of the project should only indicate an import if they are not implicit.
                Assert.Equal(expectImportInLogicalProject ? 2 : 0, projectRootElement.Imports.Count);

                // The project representation should have imports
                Assert.Equal(2, project.Imports.Count);

                ResolvedImport initialResolvedImport = project.Imports[0];
                Assert.Equal(_sdkPropsPath, initialResolvedImport.ImportedProject.FullPath);


                ResolvedImport finalResolvedImport = project.Imports[1];
                Assert.Equal(_sdkTargetsPath, finalResolvedImport.ImportedProject.FullPath);

                VerifyPropertyFromImplicitImport(project, "InitialImportProperty", _sdkPropsPath, "Hello");
                VerifyPropertyFromImplicitImport(project, "FinalImportProperty", _sdkTargetsPath, "World");

                // TODO: Check the location of the import, maybe it should point to the location of the SDK attribute?
            }
        }

        /// <summary>
        /// Verifies that when a user specifies more than one SDK that everything works as expected
        /// </summary>
        [Theory]
        [InlineData(@"
<Project Sdk=""{0};{1};{2}"">
</Project >", false)]
        [InlineData(@"
<Project>
  <Sdk Name=""{0}"" />
  <Sdk Name=""{1}"" />
  <Sdk Name=""{2}"" />
</Project>", false)]
        [InlineData(@"
<Project>
  <Import Project=""Sdk.props"" Sdk=""{0}"" />
  <Import Project=""Sdk.props"" Sdk=""{1}"" />
  <Import Project=""Sdk.props"" Sdk=""{2}"" />

  <Import Project=""Sdk.targets"" Sdk=""{0}"" />
  <Import Project=""Sdk.targets"" Sdk=""{1}"" />
  <Import Project=""Sdk.targets"" Sdk=""{2}"" />
</Project>", true)]
        public void SdkSupportsMultiple(string projectFormatString, bool expectImportInLogicalProject)
        {
            IList<string> sdkNames = new List<string>
            {
                "MSBuild.SDK.One",
                "MSBuild.SDK.Two",
                "MSBuild.SDK.Three",
            };

            foreach (string sdkName in sdkNames)
            {
                string testSdkDirectory = Directory.CreateDirectory(Path.Combine(_testSdkRoot, sdkName, "Sdk")).FullName;

                File.WriteAllText(Path.Combine(testSdkDirectory, "Sdk.props"), $"<Project><PropertyGroup><InitialImportProperty>{sdkName}</InitialImportProperty></PropertyGroup></Project>");
                File.WriteAllText(Path.Combine(testSdkDirectory, "Sdk.targets"), $"<Project><PropertyGroup><FinalImportProperty>{sdkName}</FinalImportProperty></PropertyGroup></Project>");
            }

            using (new Helpers.TemporaryEnvironment("MSBuildSDKsPath", _testSdkRoot))
            {
                string content = string.Format(projectFormatString, sdkNames[0], sdkNames[1], sdkNames[2]);

                ProjectRootElement projectRootElement = ProjectRootElement.Create(XmlReader.Create(new StringReader(content)));

                Project project = new Project(projectRootElement);

                // The XML representation of the project should indicate there are no imports
                Assert.Equal(expectImportInLogicalProject ? 6 : 0, projectRootElement.Imports.Count);

                // The project representation should have twice as many imports as SDKs
                Assert.Equal(sdkNames.Count * 2, project.Imports.Count);

                // Last imported SDK should set the value
                VerifyPropertyFromImplicitImport(project, "InitialImportProperty", Path.Combine(_testSdkRoot, sdkNames.Last(), "Sdk", "Sdk.props"), sdkNames.Last());
                VerifyPropertyFromImplicitImport(project, "FinalImportProperty", Path.Combine(_testSdkRoot, sdkNames.Last(), "Sdk", "Sdk.targets"), sdkNames.Last());
            }
        }

        [Theory]
        [InlineData(ProjectTemplateSdkAsAttribute)]
        [InlineData(ProjectTemplateSdkAsElement)]
        [InlineData(ProjectTemplateSdkAsExplicitImport)]
        public void ProjectWithSdkImportsIsCloneable(string projectFormatString)
        {
            string projectInnerContents = @"
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>netcoreapp1.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include=""**\*.cs"" />
    <EmbeddedResource Include=""**\*.resx"" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include=""Microsoft.NETCore.App"" Version=""1.0.1"" />
  </ItemGroup>";
            File.WriteAllText(_sdkPropsPath, "<Project />");
            File.WriteAllText(_sdkTargetsPath, "<Project />");

            using (new Helpers.TemporaryEnvironment("MSBuildSDKsPath", _testSdkRoot))
            {
                // Based on the new-console-project CLI template (but not matching exactly
                // should not be a deal-breaker).
                string content = string.Format(projectFormatString, SdkName, projectInnerContents);
                ProjectRootElement project = ProjectRootElement.Create(XmlReader.Create(new StringReader(content)));

                project.DeepClone();
            }
        }

        [Theory]
        [InlineData(ProjectTemplateSdkAsAttribute)]
        [InlineData(ProjectTemplateSdkAsElement)]
        [InlineData(ProjectTemplateSdkAsExplicitImport)]
        public void ProjectWithSdkImportsIsRemoveable(string projectFormatString)
        {
            string projectInnerContents = @"
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>netcoreapp1.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include=""**\*.cs"" />
    <EmbeddedResource Include=""**\*.resx"" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include=""Microsoft.NETCore.App"" Version=""1.0.1"" />
  </ItemGroup>";
            File.WriteAllText(_sdkPropsPath, " <Project />");
            File.WriteAllText(_sdkTargetsPath, "<Project />");

            using (new Helpers.TemporaryEnvironment("MSBuildSDKsPath", _testSdkRoot))
            {
                // Based on the new-console-project CLI template (but not matching exactly
                // should not be a deal-breaker).
                string content = string.Format(projectFormatString, SdkName, projectInnerContents);
                ProjectRootElement project = ProjectRootElement.Create(XmlReader.Create(new StringReader(content)));
                ProjectRootElement clone = ProjectRootElement.Create(XmlReader.Create(new StringReader(content)));

                clone.DeepCopyFrom(project);

                clone.RemoveAllChildren();
            }
        }

        /// <summary>
        /// Verifies that an error occurs when an SDK name is not in the correct format.
        /// </summary>
        [Fact]
        public void ProjectWithInvalidSdkName()
        {
            const string invalidSdkName = "SdkWithExtra/Slash/1.0.0";

            InvalidProjectFileException exception = Assert.Throws<InvalidProjectFileException>(() =>
            {
                using (new Helpers.TemporaryEnvironment("MSBuildSDKsPath", _testSdkRoot))
                {
                    string content = $@"
                    <Project Sdk=""{invalidSdkName}"">
                        <PropertyGroup>
                            <UsedToTestIfImplicitImportsAreInTheCorrectLocation>null</UsedToTestIfImplicitImportsAreInTheCorrectLocation>
                        </PropertyGroup>
                    </Project>";

                    Project project = new Project(ProjectRootElement.Create(XmlReader.Create(new StringReader(content))));
                }
            });
            
            Assert.Equal("MSB4229", exception.ErrorCode);
        }

        /// <summary>
        /// Verifies that an empty SDK attribute works and nothing is imported.
        /// </summary>
        [Theory]
        [InlineData(ProjectTemplateSdkAsAttribute, false)]
        [InlineData(ProjectTemplateSdkAsElement, true)]
        [InlineData(ProjectTemplateSdkAsExplicitImport, true)]
        public void ProjectWithEmptySdkName(string projectFormatString, bool throwsOnEvaluate)
        {
            string projectInnerContents =
                @"<PropertyGroup><UsedToTestIfImplicitImportsAreInTheCorrectLocation>null</UsedToTestIfImplicitImportsAreInTheCorrectLocation></PropertyGroup>";

            using (new Helpers.TemporaryEnvironment("MSBuildSDKsPath", _testSdkRoot))
            {
                string content = string.Format(projectFormatString, string.Empty, projectInnerContents);
                if (throwsOnEvaluate)
                {
                    Assert.Throws<InvalidProjectFileException>(
                        () => new Project(ProjectRootElement.Create(XmlReader.Create(new StringReader(content)))));
                }
                else
                {
                    var project = new Project(ProjectRootElement.Create(XmlReader.Create(new StringReader(content))));
                    Assert.Equal(0, project.Imports.Count);
                }
            }
        }

        /// <summary>
        /// Verifies that an empty SDK attribute works and nothing is imported.
        /// </summary>
        [Fact]
        public void ProjectWithEmptySdkNameElementThrows()
        {
            using (new Helpers.TemporaryEnvironment("MSBuildSDKsPath", _testSdkRoot))
            {
                string content = @"
                    <Project>
                        <Sdk Name="""" />
                        <PropertyGroup>
                            <UsedToTestIfImplicitImportsAreInTheCorrectLocation>null</UsedToTestIfImplicitImportsAreInTheCorrectLocation>
                        </PropertyGroup>
                    </Project>";

                var e =
                    Assert.Throws<InvalidProjectFileException>(() => new Project(
                        ProjectRootElement.Create(XmlReader.Create(new StringReader(content)))));

                Assert.Equal("MSB4238", e.ErrorCode);
            }
        }

        /// <summary>
        /// Verifies that an error occurs when one or more SDK names are empty.
        /// </summary>
        [Fact]
        public void ProjectWithEmptySdkNameInValidList()
        {
            const string invalidSdkName = "foo;  ;bar";

            InvalidProjectFileException exception = Assert.Throws<InvalidProjectFileException>(() =>
            {
                using (new Helpers.TemporaryEnvironment("MSBuildSDKsPath", _testSdkRoot))
                {
                    string content = $@"
                    <Project Sdk=""{invalidSdkName}"">
                        <PropertyGroup>
                            <UsedToTestIfImplicitImportsAreInTheCorrectLocation>null</UsedToTestIfImplicitImportsAreInTheCorrectLocation>
                        </PropertyGroup>
                    </Project>";

                    Project project = new Project(ProjectRootElement.Create(XmlReader.Create(new StringReader(content))));
                }
            });

            Assert.Equal("MSB4229", exception.ErrorCode);
        }

        public void Dispose()
        {
            if (Directory.Exists(_testSdkDirectory))
            {
                FileUtilities.DeleteWithoutTrailingBackslash(_testSdkDirectory, true);
            }
        }

        private void VerifyPropertyFromImplicitImport(Project project, string propertyName, string expectedContainingProjectPath, string expectedValue)
        {
            ProjectProperty property = project.GetProperty(propertyName);

            Assert.NotNull(property?.Xml?.ContainingProject?.FullPath);

            Assert.Equal(expectedContainingProjectPath, property.Xml.ContainingProject.FullPath);

            Assert.True(property.IsImported);

            Assert.Equal(expectedValue, property.EvaluatedValue);
        }
    }
}