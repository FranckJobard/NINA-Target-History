using System.IO;
using System.Text.Json;
using NINA.TargetHistory.Models;

namespace NINA.TargetHistory.Services;

public sealed class SequenceParser {
    public IReadOnlyList<SequenceTarget> Parse(string path) {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var doc = JsonDocument.Parse(stream, new JsonDocumentOptions {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });

        var result = new List<SequenceTarget>();
        Walk(doc.RootElement, path, result);
        return result;
    }

    private static void Walk(JsonElement node, string path, List<SequenceTarget> result) {
        if (node.ValueKind == JsonValueKind.Object) {
            if (IsDeepSkyObjectContainer(node) && TryReadTarget(node, path, out var target))
                result.Add(target);

            foreach (var property in node.EnumerateObject())
                Walk(property.Value, path, result);
        } else if (node.ValueKind == JsonValueKind.Array) {
            foreach (var child in node.EnumerateArray())
                Walk(child, path, result);
        }
    }

    private static bool IsDeepSkyObjectContainer(JsonElement node) {
        return node.TryGetProperty("$type", out var type)
            && type.ValueKind == JsonValueKind.String
            && (type.GetString()?.StartsWith(
                "NINA.Sequencer.Container.DeepSkyObjectContainer", StringComparison.Ordinal) ?? false);
    }

    private static bool TryReadTarget(JsonElement container, string path, out SequenceTarget target) {
        target = null!;
        if (!container.TryGetProperty("Target", out var t) || t.ValueKind != JsonValueKind.Object)
            return false;

        var name = String(t, "TargetName");
        if (string.IsNullOrWhiteSpace(name))
            name = String(container, "Name");
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var pa = Number(t, "PositionAngle");
        var (ra, dec) = ReadCoordinates(t);

        var exposures = new List<ExposureEntry>();
        if (container.TryGetProperty("ExposureInfoList", out var list)
            && list.ValueKind == JsonValueKind.Object
            && list.TryGetProperty("$values", out var values)
            && values.ValueKind == JsonValueKind.Array) {
            foreach (var e in values.EnumerateArray()) {
                var imageType = String(e, "ImageType");
                if (!string.IsNullOrEmpty(imageType)
                    && !imageType.Equals("LIGHT", StringComparison.OrdinalIgnoreCase))
                    continue;

                var filter = String(e, "Filter");
                if (string.IsNullOrWhiteSpace(filter))
                    filter = "No filter";

                exposures.Add(new ExposureEntry(
                    filter,
                    Int(e, "Count"),
                    Number(e, "ExposureTime"),
                    Number(e, "Gain"),
                    Number(e, "Offset"),
                    Math.Max(1, Int(e, "BinningX")),
                    Math.Max(1, Int(e, "BinningY"))));
            }
        }

        target = new SequenceTarget {
            SourceFile = path,
            Name = name.Trim(),
            RaDegrees = ra,
            DecDegrees = dec,
            PositionAngle = pa,
            LastWriteUtc = File.GetLastWriteTimeUtc(path),
            Exposures = exposures
        };
        return true;
    }

    private static (double ra, double dec) ReadCoordinates(JsonElement target) {
        if (!target.TryGetProperty("InputCoordinates", out var c) || c.ValueKind != JsonValueKind.Object)
            return (0, 0);

        var raHours = Number(c, "RAHours")
                    + Number(c, "RAMinutes") / 60d
                    + Number(c, "RASeconds") / 3600d;
        var decAbs = Math.Abs(Number(c, "DecDegrees"))
                   + Number(c, "DecMinutes") / 60d
                   + Number(c, "DecSeconds") / 3600d;
        var negative = c.TryGetProperty("NegativeDec", out var neg)
                       && neg.ValueKind == JsonValueKind.True;
        return (raHours * 15d, negative ? -decAbs : decAbs);
    }

    private static string? String(JsonElement e, string name)
        => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static double Number(JsonElement e, string name)
        => e.TryGetProperty(name, out var p) && p.TryGetDouble(out var n) ? n : 0;

    private static int Int(JsonElement e, string name)
        => e.TryGetProperty(name, out var p) && p.TryGetInt32(out var n) ? n : 0;
}
