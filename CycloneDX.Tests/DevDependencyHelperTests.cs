using System.Collections.Generic;
using System.Linq;
using CycloneDX.Models;
using Xunit;

namespace CycloneDX.Tests
{
    public class DevDependencyHelperTests
    {
        private static DotnetDependency Package(string name, bool isDirect = false, bool isDev = false, params string[] dependencies)
        {
            return new DotnetDependency
            {
                Name = name,
                Version = "1.0.0",
                IsDirectReference = isDirect,
                IsDevDependency = isDev,
                Scope = Component.ComponentScope.Required,
                Dependencies = dependencies.ToDictionary(d => d, _ => "1.0.0")
            };
        }

        private static IEnumerable<string> DevPackageNames(IEnumerable<DotnetDependency> packages)
            => packages.Where(p => p.IsDevDependency).Select(p => p.Name).OrderBy(n => n);

        [Fact]
        public void MarkTransitiveDevDependencies_MarksDependenciesOnlyReachableThroughDevDependencies()
        {
            var packages = new HashSet<DotnetDependency>
            {
                Package("Runtime", isDirect: true, dependencies: "Shared"),
                Package("Dev", isDirect: true, isDev: true, "DevOnly", "Shared"),
                Package("DevOnly", dependencies: "DevOnlyLeaf"),
                Package("DevOnlyLeaf"),
                Package("Shared"),
            };

            DevDependencyHelper.MarkTransitiveDevDependencies(packages);

            Assert.Equal(new[] { "Dev", "DevOnly", "DevOnlyLeaf" }, DevPackageNames(packages));
        }

        [Fact]
        public void MarkTransitiveDevDependencies_UnmarksDevDependencyThatIsRequiredByNonDevDependency()
        {
            var packages = new HashSet<DotnetDependency>
            {
                Package("Runtime", isDirect: true, dependencies: "DevAndRuntime"),
                Package("DevAndRuntime", isDirect: true, isDev: true, "Leaf"),
                Package("Leaf"),
            };

            DevDependencyHelper.MarkTransitiveDevDependencies(packages);

            Assert.Empty(DevPackageNames(packages));
        }

        [Fact]
        public void MarkTransitiveDevDependencies_TreatsNonDevPackagesWithoutParentsAsRoots()
        {
            // Project references are not listed as direct references in the assets file,
            // but anything they pull in is shipped.
            var packages = new HashSet<DotnetDependency>
            {
                Package("ReferencedProject", dependencies: "Shared"),
                Package("Dev", isDirect: true, isDev: true, "Shared"),
                Package("Shared"),
            };

            DevDependencyHelper.MarkTransitiveDevDependencies(packages);

            Assert.Equal(new[] { "Dev" }, DevPackageNames(packages));
        }

        [Fact]
        public void MarkTransitiveDevDependencies_HandlesCyclesAndMissingDependencies()
        {
            var packages = new HashSet<DotnetDependency>
            {
                Package("Dev", isDirect: true, isDev: true, "A", "NotInGraph"),
                Package("A", dependencies: "B"),
                Package("B", dependencies: "A"),
            };

            DevDependencyHelper.MarkTransitiveDevDependencies(packages);

            Assert.Equal(new[] { "A", "B", "Dev" }, DevPackageNames(packages));
        }

        [Fact]
        public void MarkTransitiveDevDependencies_MatchesDependencyNamesCaseInsensitively()
        {
            var packages = new HashSet<DotnetDependency>
            {
                Package("Runtime", isDirect: true, dependencies: "shared"),
                Package("Dev", isDirect: true, isDev: true, "SHARED"),
                Package("Shared"),
            };

            DevDependencyHelper.MarkTransitiveDevDependencies(packages);

            Assert.Equal(new[] { "Dev" }, DevPackageNames(packages));
        }

        [Fact]
        public void MergePackageSets_PackageIsOnlyDevWhenDevInEveryGraph()
        {
            var target = new HashSet<DotnetDependency>
            {
                Package("DevHere"),
                Package("DevEverywhere"),
            };
            target.First(p => p.Name == "DevHere").IsDevDependency = true;
            target.First(p => p.Name == "DevEverywhere").IsDevDependency = true;

            var testScoped = Package("DevHere");
            testScoped.Scope = Component.ComponentScope.Excluded;
            var source = new[]
            {
                testScoped,
                Package("DevEverywhere", isDev: true),
                Package("New", isDev: true),
            };

            DevDependencyHelper.MergePackageSets(target, source);

            Assert.Equal(new[] { "DevEverywhere", "New" }, DevPackageNames(target));
            // The scope of the non-dev usage is the one that describes how the package is used.
            Assert.Equal(Component.ComponentScope.Excluded, target.First(p => p.Name == "DevHere").Scope);
        }
    }
}
