using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CycloneDX.Models;
using Xunit;

namespace CycloneDX.Tests.FunctionalTests
{
    // Dependency graph of the test project:
    //
    //   Project
    //   ├── Runtime.Pkg
    //   │   ├── Shared.Pkg
    //   │   └── DevAndRuntime.Pkg
    //   ├── Dev.Tool            (PrivateAssets=all)
    //   │   ├── DevOnly.Pkg
    //   │   │   └── DevOnly.Leaf
    //   │   └── Shared.Pkg
    //   └── DevAndRuntime.Pkg   (PrivateAssets=all)
    public class Issue1105
    {
        private static readonly string AssetsJson =
            File.ReadAllText(Path.Combine("FunctionalTests", "Issue1105-TransitiveDevDependencies", "project.assets.json"));

        [Fact]
        public async Task WithoutExcludeDev_AllPackagesAreInTheBom()
        {
            var bom = await FunctionalTestHelper.Test(AssetsJson, new RunOptions());

            Assert.Equal(
                new[] { "Dev.Tool", "DevAndRuntime.Pkg", "DevOnly.Leaf", "DevOnly.Pkg", "Runtime.Pkg", "Shared.Pkg" },
                bom.Components.Select(c => c.Name).OrderBy(n => n));
        }

        [Fact]
        public async Task WithExcludeDev_TransitiveDependenciesOnlyReachableThroughDevDependenciesAreExcluded()
        {
            var bom = await FunctionalTestHelper.Test(AssetsJson, new RunOptions { excludeDev = true });

            var names = bom.Components.Select(c => c.Name).ToList();
            Assert.DoesNotContain("Dev.Tool", names);
            Assert.DoesNotContain("DevOnly.Pkg", names);
            Assert.DoesNotContain("DevOnly.Leaf", names);
        }

        [Fact]
        public async Task WithExcludeDev_TransitiveDependencySharedWithNonDevDependencyIsKept()
        {
            var bom = await FunctionalTestHelper.Test(AssetsJson, new RunOptions { excludeDev = true });

            Assert.Contains(bom.Components, c => c.Name == "Runtime.Pkg");
            Assert.Contains(bom.Components, c => c.Name == "Shared.Pkg");
            FunctionalTestHelper.AssertHasDependencyWithChild(bom, "pkg:nuget/Runtime.Pkg@1.0.0", "pkg:nuget/Shared.Pkg@1.0.0");
        }

        [Fact]
        public async Task WithExcludeDev_DevDependencyThatIsAlsoRequiredByNonDevDependencyIsKept()
        {
            var bom = await FunctionalTestHelper.Test(AssetsJson, new RunOptions { excludeDev = true });

            Assert.Contains(bom.Components, c => c.Name == "DevAndRuntime.Pkg");
            FunctionalTestHelper.AssertHasDependencyWithChild(bom, "pkg:nuget/Runtime.Pkg@1.0.0", "pkg:nuget/DevAndRuntime.Pkg@1.0.0");
        }

        [Fact]
        public async Task WithExcludeDev_EveryDependencyRefPointsToAComponentOrTheMetadataComponent()
        {
            var bom = await FunctionalTestHelper.Test(AssetsJson, new RunOptions { excludeDev = true });

            var knownRefs = bom.Components.Select(c => c.BomRef).Append(bom.Metadata.Component.BomRef).ToHashSet();
            var allRefs = bom.Dependencies.Select(d => d.Ref)
                .Concat(bom.Dependencies.SelectMany(d => d.Dependencies ?? []).Select(d => d.Ref));

            Assert.All(allRefs, r => Assert.Contains(r, knownRefs));
        }
    }
}
