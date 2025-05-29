using System;

namespace Mister.Version.Core.Services
{
    /// <summary>
    /// Factory for creating standardized loggers across different contexts
    /// </summary>
    public static class LoggerFactory
    {
        /// <summary>
        /// Creates a logger for CLI contexts with console output
        /// </summary>
        /// <param name="debug">Whether debug logging is enabled</param>
        /// <param name="jsonOutput">Whether output is in JSON format (affects where debug/warning messages go)</param>
        /// <returns>Logger action</returns>
        public static Action<string, string> CreateCliLogger(bool debug, bool jsonOutput = false)
        {
            return (level, message) =>
            {
                if (debug || level == "Error" || level == "Warning")
                {
                    var formattedMessage = $"[{level}] {message}";
                    
                    // When outputting JSON, warnings and debug info must go to stderr
                    if (jsonOutput)
                    {
                        Console.Error.WriteLine(formattedMessage);
                    }
                    else
                    {
                        Console.WriteLine(formattedMessage);
                    }
                }
            };
        }

        /// <summary>
        /// Creates a logger for CLI report contexts (always uses stderr to avoid interfering with report output)
        /// </summary>
        /// <param name="debug">Whether debug logging is enabled</param>
        /// <returns>Logger action</returns>
        public static Action<string, string> CreateReportLogger(bool debug)
        {
            return (level, message) =>
            {
                if (debug || level == "Error" || level == "Warning")
                {
                    // Always output debug/warning/error messages to stderr to avoid interfering with report output
                    Console.Error.WriteLine($"[{level}] {message}");
                }
            };
        }

        /// <summary>
        /// Creates a logger with level-based filtering
        /// </summary>
        /// <param name="outputAction">Action to call for each log message (level, message)</param>
        /// <param name="debug">Whether debug logging is enabled</param>
        /// <param name="extraDebug">Whether extra debug logging is enabled</param>
        /// <returns>Logger action</returns>
        public static Action<string, string> CreateFilteredLogger(Action<string, string> outputAction, bool debug, bool extraDebug = false)
        {
            return (level, message) =>
            {
                bool shouldLog = level switch
                {
                    "Error" => true,
                    "Warning" => true,
                    "Info" => true,
                    "Debug" when debug || extraDebug => true,
                    _ => false
                };

                if (shouldLog)
                {
                    outputAction(level, $"[{level}] {message}");
                }
            };
        }

        /// <summary>
        /// Creates a silent logger that discards all messages
        /// </summary>
        /// <returns>Logger action that does nothing</returns>
        public static Action<string, string> CreateSilentLogger()
        {
            return (level, message) => { /* Do nothing */ };
        }

        /// <summary>
        /// Creates a custom logger with a provided output action
        /// </summary>
        /// <param name="outputAction">Action to call for each log message</param>
        /// <param name="debug">Whether debug logging is enabled</param>
        /// <returns>Logger action</returns>
        public static Action<string, string> CreateCustomLogger(Action<string, string> outputAction, bool debug)
        {
            return (level, message) =>
            {
                if (debug || level == "Error" || level == "Warning")
                {
                    outputAction(level, message);
                }
            };
        }
    }
}