// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using BuildXL.Utilities.Core;

namespace BuildXL
{
    /// <summary>
    /// Loads default bxl arguments from <c>.bxlrc</c> files so that boilerplate flags
    /// (e.g. <c>/server-</c>, <c>/cacheDirectory:…</c>, <c>/c:…</c>) do not have to be
    /// repeated on every invocation.
    /// </summary>
    /// <remarks>
    /// Discovery order, all of which contribute (later sources are appended last and therefore
    /// win for "last-wins" options):
    /// <list type="number">
    /// <item><c>$HOME/.bxlrc</c> (or <c>%USERPROFILE%\.bxlrc</c>) — user-global defaults.</item>
    /// <item>The nearest <c>.bxlrc</c> found by walking up from the current working directory
    /// — project-local defaults.</item>
    /// </list>
    /// User-supplied command line arguments are appended after these so they always win.
    /// <para>
    /// Format: one argument per line. Lines whose first non-whitespace character is <c>#</c>
    /// are treated as comments and skipped. Blank lines are skipped. Leading and trailing
    /// whitespace on each line is trimmed.
    /// </para>
    /// <para>
    /// Set the environment variable <c>BXL_NO_RC=1</c> to disable rc-file loading entirely.
    /// </para>
    /// </remarks>
    internal static class BxlRc
    {
        internal const string RcFileName = ".bxlrc";
        internal const string DisableEnvVar = "BXL_NO_RC";

        /// <summary>
        /// Returns the default arguments collected from <c>.bxlrc</c> files in discovery order.
        /// Returns an empty array if no rc files are found or if rc loading is disabled.
        /// </summary>
        public static string[] LoadDefaultArgs()
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(DisableEnvVar)))
            {
                return Array.Empty<string>();
            }

            var result = new List<string>();

            string homeRc = GetHomeRcPath();
            if (homeRc != null && File.Exists(homeRc))
            {
                AppendArgsFromFile(homeRc, result);
            }

            string projectRc = FindProjectRc(Directory.GetCurrentDirectory());
            if (projectRc != null && !PathEquals(projectRc, homeRc))
            {
                AppendArgsFromFile(projectRc, result);
            }

            return result.ToArray();
        }

        private static string GetHomeRcPath()
        {
            string home = Environment.GetEnvironmentVariable("HOME")
                ?? Environment.GetEnvironmentVariable("USERPROFILE");
            return string.IsNullOrEmpty(home) ? null : Path.Combine(home, RcFileName);
        }

        private static string FindProjectRc(string startDir)
        {
            try
            {
                var dir = new DirectoryInfo(startDir);
                while (dir != null)
                {
                    string candidate = Path.Combine(dir.FullName, RcFileName);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }

                    dir = dir.Parent;
                }
            }
#pragma warning disable ERP022 // intentional: best-effort discovery; on FS failure, skip project rc.
            catch
            {
            }
#pragma warning restore ERP022

            return null;
        }

        private static void AppendArgsFromFile(string path, List<string> result)
        {
            string[] lines;
            try
            {
                lines = File.ReadAllLines(path);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Error reading bxl rc file '{path}': {ex.Message}", ex);
            }

            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }

                result.Add(line);
            }
        }

        private static bool PathEquals(string a, string b)
        {
            if (a == null || b == null)
            {
                return false;
            }

            StringComparison cmp = OperatingSystemHelper.IsWindowsOS
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            try
            {
                return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), cmp);
            }
#pragma warning disable ERP022 // intentional: fall back to string equality on canonicalization failure.
            catch
            {
                return string.Equals(a, b, cmp);
            }
#pragma warning restore ERP022
        }
    }
}
