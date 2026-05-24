// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using BuildXL.FrontEnd.GitRepository;
using Xunit;

namespace Test.BuildXL.FrontEnd.GitRepository
{
    public class GitRepositoryWorkspaceResolverTests
    {
        [Fact]
        public void TryExtractModuleName_DoubleQuotedName_Succeeds()
        {
            var path = WriteTempConfig(@"
module({
    name: ""Sdk.MyRules""
});");
            try
            {
                Assert.True(GitRepositoryWorkspaceResolver.TryExtractModuleName(path, out var name));
                Assert.Equal("Sdk.MyRules", name);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void TryExtractModuleName_SingleQuotedName_Succeeds()
        {
            var path = WriteTempConfig(@"
module({
        name: 'HelloWorld',
        projects: [ f`./Hello.dsc` ]
});");
            try
            {
                Assert.True(GitRepositoryWorkspaceResolver.TryExtractModuleName(path, out var name));
                Assert.Equal("HelloWorld", name);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void TryExtractModuleName_ExtraWhitespace_Succeeds()
        {
            var path = WriteTempConfig(@"
module({
    name    :    ""Foo.Bar.Baz""
});");
            try
            {
                Assert.True(GitRepositoryWorkspaceResolver.TryExtractModuleName(path, out var name));
                Assert.Equal("Foo.Bar.Baz", name);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void TryExtractModuleName_NoNameDeclaration_ReturnsFalse()
        {
            var path = WriteTempConfig(@"
module({
    projects: [ f`./Hello.dsc` ]
});");
            try
            {
                Assert.False(GitRepositoryWorkspaceResolver.TryExtractModuleName(path, out var name));
                Assert.Null(name);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void TryExtractModuleName_MissingFile_ReturnsFalse()
        {
            var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Assert.False(GitRepositoryWorkspaceResolver.TryExtractModuleName(path, out var name));
            Assert.Null(name);
        }

        [Fact]
        public void TryExtractModuleName_PicksFirstNameOccurrence()
        {
            // Simulates a module config that also declares per-project names; the top-level
            // module name should win because it appears first.
            var path = WriteTempConfig(@"
module({
    name: ""TopLevel"",
    projects: [
        { name: ""ShouldBeIgnored"" }
    ]
});");
            try
            {
                Assert.True(GitRepositoryWorkspaceResolver.TryExtractModuleName(path, out var name));
                Assert.Equal("TopLevel", name);
            }
            finally
            {
                File.Delete(path);
            }
        }

        private static string WriteTempConfig(string contents)
        {
            var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".module.config.dsc");
            File.WriteAllText(path, contents);
            return path;
        }
    }
}
