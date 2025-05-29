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
        /// <summary>
        /// Creates a logger for MSBuild task contexts
        /// </summary>
        /// <param name="taskLoggingHelper">MSBuild task logging helper</param>
        /// <param name="debug">Whether debug logging is enabled</param>
        /// <param name="extraDebug">Whether extra debug logging is enabled</param>
        /// <returns>Logger action</returns>
        public static Action<string, string> CreateMSBuildLogger(TaskLoggingHelper taskLoggingHelper, bool debug, bool extraDebug = false)
        {
            return (level, message) =>
            {
                var importance = level switch
                {
                    "Error" => MessageImportance.High,
                    "Warning" => MessageImportance.Normal,
                    "Info" => MessageImportance.High,
                    "Debug" when debug || extraDebug => MessageImportance.High,
                    _ => MessageImportance.Low
                };

                if (importance != MessageImportance.Low)
                {
                    taskLoggingHelper.LogMessage(importance, $"[{level}] {message}");
                }
            };
        }
    }
}