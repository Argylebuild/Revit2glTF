using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using Autodesk.Revit.DB;
using GLTFRevitExport.Extensions;

namespace GLTFRevitExport.Build {
    /// <summary>
    /// Export logger. Debug builds echo to the debugger as before; both
    /// build flavors can additionally write to a timestamped file (see
    /// GLTFExportConfigs.LogToFile / LogDirectory) so exports are
    /// diagnosable in the field without a debugger — the Revit-side
    /// equivalent of the AR app's DebugFileLogger/ArgyleLogs.
    /// Logging must never break an export: all file IO is fail-silent.
    /// </summary>
    class Logger {
        // private vars to control indented logging
        const string _indentStep = "  ";
        static int _depth = 0;

        // file sink
        const int KEEP_LOG_FILES = 10;
        const int FLUSH_EVERY_LINES = 256;
        static StreamWriter _file;
        static int _linesSinceFlush = 0;

#if DEBUG
        const bool _debugBuild = true;
#else
        const bool _debugBuild = false;
#endif

        /// <summary>
        /// True when any sink would receive messages — lets callers skip
        /// building expensive log strings.
        /// </summary>
        public static bool IsActive => _debugBuild || _file != null;

        /// <summary>
        /// Start writing log output to a new timestamped file in the given
        /// directory (created if missing). Keeps the most recent
        /// KEEP_LOG_FILES files. Closes any previously open log file.
        /// </summary>
        public static void StartFileLog(string directory) {
            StopFileLog();
            try {
                Directory.CreateDirectory(directory);
                CleanupOldLogs(directory);

                string path = Path.Combine(
                    directory,
                    $"export_{DateTime.Now:yyyyMMdd_HHmmss}.log");
                _file = new StreamWriter(path, append: false) {
                    AutoFlush = false
                };
                _file.WriteLine($"# Argyle glTF export — {DateTime.Now:O}");
            }
            catch {
                _file = null;
            }
        }

        /// <summary>
        /// Flush and close the log file, if open.
        /// </summary>
        public static void StopFileLog() {
            if (_file is null)
                return;
            try {
                _file.Flush();
                _file.Dispose();
            }
            catch { }
            _file = null;
        }

        static void CleanupOldLogs(string directory) {
            try {
                var stale = new DirectoryInfo(directory)
                    .GetFiles("export_*.log")
                    .OrderByDescending(f => f.CreationTimeUtc)
                    .Skip(KEEP_LOG_FILES - 1);
                foreach (var file in stale)
                    file.Delete();
            }
            catch { }
        }

        /// <summary>
        /// Log debug message with element info
        /// </summary>
        /// <param name="message">Debug message</param>
        /// <param name="e">Target Element</param>
        public static void LogElement(string message, Element e) {
            if (!IsActive)
                return;

            if (e != null)
            {
                message +=
                    $"\n└ id={e.IdIntCompatible()} " +
                        $"name={e.Name} " +
                        $"type={e.GetType()} " +
                        $"category={e.Category?.Name}";
            }
            Log(message);
        }

        /// <summary>
        /// Log debug message
        /// </summary>
        /// <param name="message">Debug message</param>
        public static void Log(string message) {
            if (!IsActive)
                return;

            // ++ or -- the level depending on the message
            if (message.StartsWith("-"))
                _depth--;

            // indent the message based on the current depth
            string indent = "";
            for (int i = 0; i < _depth; i++)
                indent += _indentStep;

            // add the indent to all lines of the message
            string formattedMessage =
                string.Join("\n", message.Split('\n').Select(x => indent + x));

            if (message.StartsWith("+"))
                _depth++;

            if (_file != null) {
                try {
                    _file.WriteLine(formattedMessage);
                    if (++_linesSinceFlush >= FLUSH_EVERY_LINES) {
                        _file.Flush();
                        _linesSinceFlush = 0;
                    }
                }
                catch { }
            }

#if DEBUG
            Debug.WriteLine(formattedMessage);
#endif
        }

        /// <summary>
        /// Reset the logger level and internal static data
        /// </summary>
        public static void Reset() {
            _depth = 0;
        }
    }
}
