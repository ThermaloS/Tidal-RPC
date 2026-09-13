using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace TidalRpc;

public static class IssueReport
{
    public const int MaxIssueUrlLength = 1900;
    public static string Create(AppSettings settings, PlaybackSnapshot? track, string history, string description = "", bool includeSong = false)
    {
        var report = new StringBuilder();
        report.AppendLine("## What happened").AppendLine().AppendLine(Clean(description, 1000)).AppendLine();
        report.AppendLine("## Diagnostics").AppendLine().AppendLine("```text");
        report.AppendLine($"TIDAL RPC {ReleaseConfiguration.Version} | Windows {Environment.OSVersion.Version} | {RuntimeInformation.ProcessArchitecture}");
        report.AppendLine($"Sharing: {settings.Enabled}; playback: {track?.Status.ToString() ?? "Not detected"}; country: {settings.Country}");
        report.AppendLine($"Artwork: {settings.ShowArtwork}; timer: {settings.ShowPlaybackTime}; link: {settings.ShowTidalButton}; status: {settings.StatusText}");
        if (includeSong && track is not null) report.AppendLine($"Current song: {Clean(track.Title, 100)} / {Clean(track.Artist, 100)}");
        report.AppendLine("```").AppendLine().AppendLine("## Recent events").AppendLine().AppendLine("```text");
        var events = SafeEvents(history).TakeLast(60).ToArray();
        if (events.Length == 0) report.AppendLine("No recent events.");
        foreach (var entry in events) report.AppendLine(entry);
        report.AppendLine("```");
        return report.ToString();
    }

    // Export selected fields, not arbitrary log text. Track history, paths, URLs,
    // account identifiers, tokens and unknown messages never enter the report.
    public static IEnumerable<string> SafeEvents(string history)
    {
        foreach (var line in history.Split('\n').TakeLast(500))
        {
            var match = Regex.Match(line, @"^(\d{2}:\d{2}:\d{2}\.\d{3}) \[([^\]]+)\] (.*)$");
            if (!match.Success) continue;
            var source = match.Groups[2].Value;
            if (source is not ("App" or "Settings" or "Playback" or "Artwork" or "Discord" or "Discord SEND" or "Discord ACK" or "Artwork SEND" or "Artwork ACK" or "Artwork HTTP")) continue;
            var message = match.Groups[3].Value;
            var fields = Regex.Matches(message, @"\b(written|clear|match|artwork|type|code|nonce|status|bytes)=(True|False|true|false|\d+)\b")
                .Select(m => m.Value).Take(10).ToList();
            var attempt = Regex.Match(message, @"^Lookup (\d)/6:");
            if (attempt.Success) fields.Add("attempt=" + attempt.Groups[1].Value + "/6");
            var playback = Regex.Match(message, @"^(Playing|Paused|Stopped):");
            if (playback.Success) fields.Add(playback.Groups[1].Value);
            var exception = Regex.Match(message, @"\b[A-Za-z]{1,60}Exception\b");
            if (exception.Success) fields.Add(exception.Value);
            if (fields.Count == 0)
            {
                if (message.StartsWith("Started;", StringComparison.Ordinal)) fields.Add("Started");
                else if (message == "TIDAL session removed") fields.Add("Session removed");
                else if (message.StartsWith("Lookup cancelled:", StringComparison.Ordinal)) fields.Add("Lookup cancelled");
                else if (message.StartsWith("Rate limited until ", StringComparison.Ordinal)) fields.Add("Rate limited");
                else if (source == "Discord") fields.Add(message == "Discord connected" ? "Connected" : "Connection needs attention");
                else continue;
            }
            yield return $"{match.Groups[1].Value} [{source}] {string.Join(' ', fields)}";
        }
    }

    public static Uri CreateIssueUri(Uri repository, string report)
    {
        var valid = ReleaseConfiguration.RepositoryUri(repository.AbsoluteUri) ?? throw new ArgumentException("Use a GitHub repository URL.");
        var prefix = new Uri(valid, "issues/new").AbsoluteUri + "?title=" + Uri.EscapeDataString("TIDAL RPC: bug report") + "&body=";
        var body = report;
        var omitted = false;
        const string note = "\n\nAdditional details were copied to the clipboard. Paste them here if needed.";
        while (prefix.Length + Uri.EscapeDataString(body + (omitted ? note : "")).Length > MaxIssueUrlLength)
        {
            // Remove oldest event lines first, retaining complete Markdown fences.
            var eventStart = body.IndexOf("## Recent events", StringComparison.Ordinal);
            var firstEvent = eventStart < 0 ? -1 : body.IndexOf('\n', body.IndexOf("```text", eventStart, StringComparison.Ordinal)) + 1;
            var end = firstEvent > 0 ? body.IndexOf('\n', firstEvent) : -1;
            if (end > firstEvent && !body.AsSpan(firstEvent, end - firstEvent).StartsWith("```")) body = body.Remove(firstEvent, end - firstEvent + 1);
            else
            {
                // A long user description is kept in the full copied report.
                var diagnostics = body.IndexOf("## Diagnostics", StringComparison.Ordinal);
                if (diagnostics > 0) body = "## What happened\n\nSee the full copied report.\n\n" + body[diagnostics..];
                else throw new InvalidOperationException("The report is too large to open in the browser.");
                if (prefix.Length + Uri.EscapeDataString(body + note).Length > MaxIssueUrlLength)
                    body = "## What happened\n\nDescribe the problem here. Full diagnostic details were copied to the clipboard.\n\n" + $"TIDAL RPC {ReleaseConfiguration.Version}\n";
            }
            omitted = true;
        }
        return new Uri(prefix + Uri.EscapeDataString(body + (omitted ? note : "")));
    }
    private static string Clean(string text, int limit) => new(text.Where(c => !char.IsControl(c)).Take(limit).Select(c => c == '`' ? '\'' : c).ToArray());
}
