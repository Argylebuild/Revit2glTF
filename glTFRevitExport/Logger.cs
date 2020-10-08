﻿using System.Linq;
using System.Diagnostics;
using Autodesk.Revit.DB;

namespace GLTFRevitExport {
    internal class Logger {
        // private vars to control indented logging
        private const string _indentStep = "  ";
        private static int _depth = 0;

        /// <summary>
        /// Log debug message with element info
        /// </summary>
        /// <param name="message">Debug message</param>
        /// <param name="e">Target Element</param>
        internal static void LogElement(string message, Element e) {
#if DEBUG
            if (e != null)
                message +=
                    $"\n└ id={e.Id.IntegerValue} " +
                        $"name={e.Name} " +
                        $"type={e.GetType()} " +
                        $"category={e.Category?.Name}";
            Log(message);
#endif
        }

        /// <summary>
        /// Log debug message
        /// </summary>
        /// <param name="message">Debug message</param>
        internal static void Log(string message) {
#if DEBUG
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

            Debug.WriteLine(formattedMessage);
#endif
        }

        /// <summary>
        /// Reset the logger level and internal static data
        /// </summary>
        internal static void Reset() => _depth = 0;
    }
}