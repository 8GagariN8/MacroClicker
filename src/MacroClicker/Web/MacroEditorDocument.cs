using System.Text.Json;
using System.Text.Json.Nodes;

namespace MacroClicker;

// The editor carries temporary window handles. Storage intentionally does not.
internal static class MacroEditorDocument
{
    public static JsonNode Export(MacroDocument document)
    {
        var json = JsonSerializer.SerializeToNode(document, MacroStorage.Options)!;
        for (var i = 0; i < document.Nodes.Count; i++)
        {
            json["Nodes"]![i]!["TargetId"] = document.Nodes[i].SessionTarget?.Handle.ToInt64().ToString();
            for (var r = 0; r < document.Nodes[i].PeriodicActions.Count; r++)
                json["Nodes"]![i]!["PeriodicActions"]![r]!["Node"]!["TargetId"] =
                    document.Nodes[i].PeriodicActions[r].Node.SessionTarget?.Handle.ToInt64().ToString();
        }
        return json;
    }

    public static MacroDocument Import(JsonElement json, MacroDocument previousDocument,
        IReadOnlyDictionary<string, WindowTarget> windows)
    {
        var doc = json.Deserialize<MacroDocument>(MacroStorage.Options) ?? throw new InvalidDataException("JSON пуст.");
        if (doc.Version != 3) throw new InvalidDataException("В редакторе нужна Version: 3. Старые файлы открывайте через импорт.");
        MacroStorage.Validate(doc);
        var raw = json.GetProperty("Nodes");
        var previous = previousDocument.AllNodes().ToArray();
        void Bind(MacroNode node, JsonElement source)
        {
            if (source.TryGetProperty("TargetId", out var id) && id.ValueKind == JsonValueKind.String &&
                windows.TryGetValue(id.GetString()!, out var target) && target.ProcessName == node.ProcessName && target.Title == node.WindowTitle)
                node.SessionTarget = target;
            else
            {
                var matches = previous.Where(n => n.ProcessName == node.ProcessName && n.WindowTitle == node.WindowTitle)
                    .Select(n => n.SessionTarget).OfType<WindowTarget>().Distinct().ToArray();
                // An edited JSON document without a handle must never pick an arbitrary
                // window when several identical titles/processes were bound previously.
                if (matches.Length == 1) node.SessionTarget = matches[0];
            }
        }
        for (var i = 0; i < doc.Nodes.Count; i++)
        {
            Bind(doc.Nodes[i], raw[i]);
            for (var r = 0; r < doc.Nodes[i].PeriodicActions.Count; r++)
                Bind(doc.Nodes[i].PeriodicActions[r].Node, raw[i].GetProperty("PeriodicActions")[r].GetProperty("Node"));
        }
        return doc;
    }
}
