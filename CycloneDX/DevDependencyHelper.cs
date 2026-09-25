// This file is part of CycloneDX Tool for .NET
//
// Licensed under the Apache License, Version 2.0 (the “License”);
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an “AS IS” BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
// SPDX-License-Identifier: Apache-2.0
// Copyright (c) OWASP Foundation. All Rights Reserved.
using System;
using System.Collections.Generic;
using System.Linq;
using CycloneDX.Models;

namespace CycloneDX
{
    internal static class DevDependencyHelper
    {
        /// <summary>
        /// Propagates <see cref="DotnetDependency.IsDevDependency"/> through a dependency graph.
        /// On input the flag marks direct references declared as development dependencies
        /// (e.g. <c>PrivateAssets="all"</c>). On output it marks every package that is reachable
        /// only through development dependencies. A package that is also reachable from a
        /// non-development root is not a development dependency, even if it was flagged on input.
        /// </summary>
        /// <param name="packages">
        /// A single resolved dependency graph (e.g. one target of a project.assets.json), in which
        /// every package name occurs at most once.
        /// </param>
        internal static void MarkTransitiveDevDependencies(ICollection<DotnetDependency> packages)
        {
            var devRoots = packages.Where(p => p.IsDevDependency).ToList();
            if (devRoots.Count == 0)
            {
                return;
            }

            var packagesByName = packages
                .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var dependedUpon = packages
                .SelectMany(p => p.Dependencies?.Keys ?? Enumerable.Empty<string>())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Anything a non-dev direct reference needs is shipped. Non-dev packages that nothing
            // depends on (e.g. project references, or graphs without direct reference information)
            // are treated as roots as well, so we never exclude something we can't prove is dev-only.
            var nonDevRoots = packages.Where(p => !p.IsDevDependency && (p.IsDirectReference || !dependedUpon.Contains(p.Name)));

            var reachableFromNonDev = FindReachable(nonDevRoots, packagesByName);
            var reachableFromDev = FindReachable(devRoots, packagesByName);

            foreach (var package in packages)
            {
                package.IsDevDependency = reachableFromDev.Contains(package) && !reachableFromNonDev.Contains(package);
            }
        }

        /// <summary>
        /// Adds <paramref name="source"/> to <paramref name="target"/>. A package that exists in both
        /// is only a development dependency if it is one in both, since otherwise it is shipped by
        /// at least one of the merged graphs.
        /// </summary>
        internal static void MergePackageSets(HashSet<DotnetDependency> target, IEnumerable<DotnetDependency> source)
        {
            foreach (var package in source)
            {
                if (!target.TryGetValue(package, out var existing))
                {
                    target.Add(package);
                }
                else if (existing.IsDevDependency && !package.IsDevDependency)
                {
                    existing.IsDevDependency = false;
                    existing.Scope = package.Scope;
                }
            }
        }

        private static HashSet<DotnetDependency> FindReachable(IEnumerable<DotnetDependency> roots, Dictionary<string, DotnetDependency> packagesByName)
        {
            var reachable = new HashSet<DotnetDependency>();
            var queue = new Queue<DotnetDependency>(roots);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (!reachable.Add(current))
                {
                    continue;
                }

                foreach (var dependencyName in current.Dependencies?.Keys ?? Enumerable.Empty<string>())
                {
                    if (packagesByName.TryGetValue(dependencyName, out var dependency))
                    {
                        queue.Enqueue(dependency);
                    }
                }
            }

            return reachable;
        }
    }
}
