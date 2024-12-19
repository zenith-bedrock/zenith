using System.Text.RegularExpressions;
using Zenith.Raknet.Enumerator;
using Zenith.Raknet.Log;

namespace zenith.Log;

public partial class Logger : ILogger
{
        public LogLevel LogLevel { get; set; } = LogLevel.All;
        public string Format { get; set; } = "<b>{level}</b> \u2192 {message}";

        public Dictionary<LogLevel, string> CustomLevelColorMap { get; set; } = LevelColorMap;

        private static readonly Dictionary<string, string> ColorMap = new()
        {
            { "black", "\x1b[30m" },
            { "red", "\x1b[31m" },
            { "green", "\x1b[32m" },
            { "yellow", "\x1b[33m" },
            { "blue", "\x1b[34m" },
            { "magenta", "\x1b[35m" },
            { "cyan", "\x1b[36m" },
            { "white", "\x1b[37m" }
        };

        private static readonly Dictionary<LogLevel, string> LevelColorMap = new()
        {
            { LogLevel.Debug, "cyan" },
            { LogLevel.Info, "green" },
            { LogLevel.Warning, "yellow" },
            { LogLevel.Error, "red" }
        };

        public bool IsValid(LogLevel level)
        {
            return (LogLevel & level) != 0;
        }

        public void Log(LogLevel level, string message)
        {
            if (!IsValid(level)) return;

            var formattedMessage = FormatMessage(level, message);
            Console.WriteLine(formattedMessage);
        }

        private string FormatMessage(LogLevel level, string message)
        {
            var levelStr = level.ToString().ToUpper();
            var color = CustomLevelColorMap.GetValueOrDefault(level, "white");

            var formattedMessage = Format
                .Replace("{level}", $"<c=\"{color}\">{levelStr}</c>")
                .Replace("{message}", message);

            return ApplyHtmlFormatting(formattedMessage);
        }

        private string ApplyHtmlFormatting(string message)
        {
            var formattedMessage = LinkRegex().Replace(message, m =>
                $"\x1b]8;;{m.Groups[1].Value}\u001b\\{m.Groups[2].Value}\x1b]8;;\u001b\\");

            formattedMessage = ColorRegex().Replace(formattedMessage, Colorize);
            formattedMessage = ShortColorRegex().Replace(formattedMessage, Colorize);

            return formattedMessage
                .Replace("<b>", "\x1b[1m").Replace("</b>", "\x1b[22m")
                .Replace("<i>", "\x1b[3m").Replace("</i>", "\x1b[23m")
                .Replace("<u>", "\x1b[4m").Replace("</u>", "\x1b[24m")
                .Replace("<s>", "\x1b[9m").Replace("</s>", "\x1b[29m")
                .Replace("<br>", "\n");
        }

        private static string Colorize(Match match)
        {
            const string reset = "\x1b[0m";
            return ColorMap.TryGetValue(match.Groups[1].Value.ToLower(), out var color)
                ? color + match.Groups[2].Value + reset
                : match.Groups[2].Value;
        }

        [GeneratedRegex(@"<link=""(.+?)"">(.+?)</link>")]
        private static partial Regex LinkRegex();

        [GeneratedRegex(@"<color=""([^""]+)"">([^<]+)</color>")]
        private static partial Regex ColorRegex();

        [GeneratedRegex(@"<c=""([^""]+)"">([^<]+)</c>")]
        private static partial Regex ShortColorRegex();

        public void Clear() => Console.Clear();

        public void Debug(string message) => Log(LogLevel.Debug, message);

        public void Info(string message) => Log(LogLevel.Info, message);

        public void Warning(string message) => Log(LogLevel.Warning, message);

        public void Error(string message) => Log(LogLevel.Error, message);
}