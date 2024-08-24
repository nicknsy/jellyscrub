using System.Collections;
using System.Text.Json;
using System.Web;

namespace Nick.Plugin.Jellyscrub.Conversion;

public class PrettyLittleLogger
{
    private readonly List<LogMessage> _messages = new();

    public void LogSynchronized(string text, LogColor color)
    {
        ICollection col = _messages;
        lock (col.SyncRoot)
        {
            _messages.Add(new LogMessage { Text = HttpUtility.HtmlEncode(text), Color = _colorToHTML[color] });
        }
    }

    public void ClearSynchronized()
    {
        ICollection col = _messages;
        lock (col.SyncRoot)
        {
            _messages.Clear();
        }
    }

    public string ReadSynchronized()
    {
        ICollection col = _messages;
        lock (col.SyncRoot)
        {
            return JsonSerializer.Serialize(_messages);
        }
    }

    private class LogMessage
    {
        public string Text { get; set; }
        public string? Color { get; set; }
    }

    public enum LogColor
    {
        Red,
        Green,
        Blue,

        // Aliases
        Info = Blue,
        Sucess = Green,
        Error = Red
    }

    private static readonly Dictionary<LogColor, string> _colorToHTML = new Dictionary<LogColor, string>
    {
        { LogColor.Red, "#f58080" },
        { LogColor.Green, "#5eb955" },
        { LogColor.Blue, "#5abbdd" }
    };
}
