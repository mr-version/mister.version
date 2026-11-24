using System;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Mister.Version
{
    /// <summary>
    /// Factory for creating MSBuild-specific loggers
    /// </summary>
    public static class MSBuildLoggerFactory
    {
        /// <summary>Warning code for problems reported by the versioning core.</summary>
        public const string CoreWarningCode = "MRV001";

        /// <summary>Warning code for errors reported by the versioning core (the build continues with a fallback).</summary>
        public const string CoreErrorCode = "MRV002";

        /// <summary>
        /// Creates a logger for MSBuild task contexts.
        /// Core warnings and errors are reported as real MSBuild warnings (so they show in the build summary and
        /// honor -warnaserror / -nowarn:MRV001); information goes to normal verbosity and debug output to
        /// detailed verbosity unless MonoRepoDebug/MonoRepoExtraDebug is set.
        /// </summary>
        /// <param name="taskLoggingHelper">MSBuild task logging helper</param>
        /// <param name="debug">Whether debug logging is enabled</param>
        /// <param name="extraDebug">Whether extra debug logging is enabled</param>
        /// <returns>Logger action</returns>
        public static Action<string, string> CreateMSBuildLogger(TaskLoggingHelper taskLoggingHelper, bool debug, bool extraDebug = false)
        {
            var verbose = debug || extraDebug;
            return (level, message) =>
            {
                switch (level)
                {
                    case "Error":
                        taskLoggingHelper.LogWarning(null, CoreErrorCode, null, null, 0, 0, 0, 0, "[Mister.Version] " + message);
                        break;
                    case "Warning":
                        taskLoggingHelper.LogWarning(null, CoreWarningCode, null, null, 0, 0, 0, 0, "[Mister.Version] " + message);
                        break;
                    case "Info":
                        taskLoggingHelper.LogMessage(verbose ? MessageImportance.High : MessageImportance.Normal, $"[Info] {message}");
                        break;
                    default:
                        taskLoggingHelper.LogMessage(verbose ? MessageImportance.High : MessageImportance.Low, $"[{level}] {message}");
                        break;
                }
            };
        }
    }
}
