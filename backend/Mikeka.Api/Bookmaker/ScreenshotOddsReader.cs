using System.Globalization;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Beta;
using Anthropic.Models.Beta.Messages;
using Microsoft.Extensions.Options;

namespace Mikeka.Api.Bookmaker;

/// <summary>"Anthropic" section in appsettings: the API key (keep it in appsettings.Development.json) and model.</summary>
public class AnthropicOptions
{
    public string ApiKey { get; set; } = "";
    /// <summary>
    /// Workspace ID ("wrkspc_…", Settings → Workspaces on platform.claude.com). Needed only when the key is not tied to a workspace.
    /// </summary>
    public string WorkspaceId { get; set; } = "";
    public string Model { get; set; } = "claude-opus-5-5";
    /// <summary>Read every screenshot twice and keep only the rows both reads agree on.</summary>
    public bool ReadTwice { get; set; } = true;
}

/// <summary>One row read from a market block: "Under 11.5 1.14", or for an interval "no event in minutes 1-15" (line 0.5).</summary>
public record ReadRow(string Side, decimal Line, decimal Odds, string? Interval);

/// <summary>Centre of an outcome cell in a screenshot (pixels), with the odds and label read from it.</summary>
public record CellLocation(double X, double Y, decimal Odds, string Label);

/// <summary>
/// Reads odds from screenshots of a betting site's market grid (Coldbet draws its odds on a canvas, so there is no text to read).
/// Sends the images to Claude and gets the rows of one block back as structured JSON. Reading only — it never clicks anything.
/// </summary>
public class ScreenshotOddsReader(IOptions<AnthropicOptions> options, ILogger<ScreenshotOddsReader> log)
{
    private AnthropicClient? _client;

    public bool Configured => !string.IsNullOrWhiteSpace(options.Value.ApiKey) && !options.Value.ApiKey.StartsWith("SET_IN");

    private AnthropicClient Client => _client ??= string.IsNullOrWhiteSpace(options.Value.WorkspaceId)
        ? new AnthropicClient { ApiKey = options.Value.ApiKey }
        : (AnthropicClient)new AnthropicClient { ApiKey = options.Value.ApiKey }.WithOptions(o =>
        {
            o.ExtraHeaders = new Dictionary<string, string> { ["anthropic-workspace-id"] = options.Value.WorkspaceId.Trim() };
            return o;
        });

    /// <summary>
    /// "Nothing happens in this interval" rows: "16-30 Mins - No" (interval 16-30, line 0.5) and "Under 1.5 In 60 Minute"
    /// (interval 1-60, line 1.5).
    /// </summary>
    public Task<List<ReadRow>> ReadIntervalsAsync(IReadOnlyList<byte[]> screenshots, CancellationToken ct) =>
        ReadAgreedAsync(screenshots,
            "Find the interval markets for the whole match (not for one team). Return two kinds of rows: " +
            "(a) rows like \"16-30 Mins - No\" as side \"Under\", line 0.5, interval \"16-30\"; " +
            "(b) rows like \"Under 0.5 In 15 Minute\" as side \"Under\", that line, interval \"1-15\". " +
            "Use the odds shown next to each row. Skip \"Yes\" rows, Over rows and team rows.", ct);

    /// <summary>
    /// Where to click: the centre of the outcome cell described by <paramref name="target"/>
    /// (e.g. the cell labelled "Under 9.5" in the block titled "Total. Corners"), in pixels of <paramref name="screenshot"/>,
    /// with the odds shown in it. Null if it is not in this picture.
    /// </summary>
    /// <param name="above">The previous screenshot (scrolled a bit higher, overlapping), where the block's title may be; or null.</param>
    public async Task<CellLocation?> LocateCellAsync(byte[] screenshot, byte[]? above, string target, CancellationToken ct)
    {
        if (!Configured) throw new InvalidOperationException("Set Anthropic:ApiKey in appsettings.Development.json: Coldbet odds are found from screenshots.");
        var (width, height) = PngSize(screenshot);
        var content = new List<BetaContentBlockParam>();
        if (above is not null)
            content.Add(new BetaImageBlockParam { Source = new BetaBase64ImageSource { Data = Convert.ToBase64String(above), MediaType = MediaType.ImagePng } });
        content.Add(new BetaImageBlockParam { Source = new BetaBase64ImageSource { Data = Convert.ToBase64String(screenshot), MediaType = MediaType.ImagePng } });
        content.Add(new BetaTextBlockParam
        {
            Text = (above is null
                       ? $"This {width}x{height} pixel screenshot shows part of the market grid of a football match on a betting site. "
                       : "These two screenshots show the market grid of a football match on a betting site. The first is the part just above " +
                         $"the second (the page was scrolled down between them, so they overlap). The second is {width}x{height} pixels. " +
                         "A block's title may be in the first picture while its cells continue into the second. ") +
                   $"Find {target}. " +
                   "Ignore blocks with other titles (team totals such as \"Total 1.\", halves, combined markets). " +
                   $"Return the x and y pixel coordinates of the centre of that cell in the {(above is null ? "" : "second ")}image, the odds printed in the cell, " +
                   "and the cell's label exactly as shown. Report found=true only if the cell is fully visible in " +
                   $"{(above is null ? "this image" : "the second image")} and you can tell it belongs to that block " +
                   "(its title visible above it, possibly in the first picture); otherwise return found=false.",
        });
        var response = await Client.Beta.Messages.Create(new MessageCreateParams
        {
            Model = options.Value.Model,
            MaxTokens = 1024,
            Betas = ["server-side-fallback-2026-06-01"],
            Fallbacks = new List<BetaFallbackParam> { new() { Model = "claude-opus-4-8" } },
            OutputConfig = new BetaOutputConfig { Effort = Effort.Low, Format = new BetaJsonOutputFormat { Schema = LocateSchema } },
            Messages = [new BetaMessageParam { Role = Role.User, Content = content }],
        }, ct);
        if (response.StopReason == "refusal") return null;
        var json = string.Concat(response.Content.Select(b => b.TryPickText(out var t) ? t.Text : ""));
        log.LogInformation("Screenshot read for {Target}: {Answer}", target, json.Length > 300 ? json[..300] : json);
        var loc = ParseLocation(json);
        // A point outside the picture is a bad read, never a place to click.
        return loc is { } l && l.X > 0 && l.Y > 0 && l.X < width && l.Y < height ? loc : null;
    }

    /// <summary>Width and height from a PNG header.</summary>
    internal static (int width, int height) PngSize(byte[] png) =>
        png.Length < 24 ? (0, 0) : (System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)),
                                    System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)));

    public static CellLocation? ParseLocation(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        if (!r.TryGetProperty("found", out var found) || !found.GetBoolean()) return null;
        return new CellLocation(r.GetProperty("x").GetDouble(), r.GetProperty("y").GetDouble(),
            Math.Round(r.GetProperty("odds").GetDecimal(), 3), r.GetProperty("label").GetString() ?? "");
    }

    private static readonly Dictionary<string, JsonElement> LocateSchema = new()
    {
        ["type"] = JsonSerializer.SerializeToElement("object"),
        ["additionalProperties"] = JsonSerializer.SerializeToElement(false),
        ["required"] = JsonSerializer.SerializeToElement(new[] { "found", "x", "y", "odds", "label" }),
        ["properties"] = JsonSerializer.SerializeToElement(new
        {
            found = new { type = "boolean" },
            x = new { type = "number" },
            y = new { type = "number" },
            odds = new { type = "number" },
            label = new { type = "string" },
        }),
    };

    private async Task<List<ReadRow>> ReadAgreedAsync(IReadOnlyList<byte[]> screenshots, string task, CancellationToken ct)
    {
        if (!Configured) { log.LogWarning("Screenshot odds reading is off: set Anthropic:ApiKey in appsettings.Development.json"); return []; }
        var first = await ReadOnceAsync(screenshots, task, ct);
        if (!options.Value.ReadTwice || first.Count == 0) return first;
        var second = await ReadOnceAsync(screenshots, task, ct);
        // Keep only rows that both reads report identically (side, line, odds, interval).
        var agreed = first.Where(r => second.Contains(r)).Distinct().ToList();
        if (agreed.Count < first.Count)
            log.LogInformation("Screenshot reads disagreed on {Count} row(s); those rows are ignored", first.Count - agreed.Count);
        return agreed;
    }

    private async Task<List<ReadRow>> ReadOnceAsync(IReadOnlyList<byte[]> screenshots, string task, CancellationToken ct)
    {
        var content = new List<BetaContentBlockParam>();
        foreach (var png in screenshots)
            content.Add(new BetaImageBlockParam { Source = new BetaBase64ImageSource { Data = Convert.ToBase64String(png), MediaType = MediaType.ImagePng } });
        content.Add(new BetaTextBlockParam
        {
            Text = "These screenshots show the market grid of a football match on a betting site (top to bottom, possibly overlapping). " +
                   task + " If the block or rows are not visible, return found=false and no rows. Never guess a number you cannot read.",
        });

        var response = await Client.Beta.Messages.Create(new MessageCreateParams
        {
            Model = options.Value.Model,
            MaxTokens = 4096,
            Betas = ["server-side-fallback-2026-06-01"],
            Fallbacks = new List<BetaFallbackParam> { new() { Model = "claude-opus-4-8" } },
            OutputConfig = new BetaOutputConfig { Effort = Effort.Low, Format = new BetaJsonOutputFormat { Schema = Schema } },
            Messages = [new BetaMessageParam { Role = Role.User, Content = content }],
        }, ct);

        if (response.StopReason == "refusal")
        {
            log.LogWarning("Screenshot read was declined: {Details}", response.StopDetails?.Explanation);
            return [];
        }
        var json = string.Concat(response.Content.Select(b => b.TryPickText(out var t) ? t.Text : ""));
        return Parse(json);
    }

    private static readonly Dictionary<string, JsonElement> Schema = new()
    {
        ["type"] = JsonSerializer.SerializeToElement("object"),
        ["additionalProperties"] = JsonSerializer.SerializeToElement(false),
        ["required"] = JsonSerializer.SerializeToElement(new[] { "found", "rows" }),
        ["properties"] = JsonSerializer.SerializeToElement(new
        {
            found = new { type = "boolean" },
            rows = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    additionalProperties = false,
                    required = new[] { "side", "line", "odds", "interval" },
                    properties = new
                    {
                        side = new { type = "string", @enum = new[] { "Over", "Under" } },
                        line = new { type = "number" },
                        odds = new { type = "number" },
                        interval = new { type = new[] { "string", "null" } },
                    },
                },
            },
        }),
    };

    public static List<ReadRow> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("found", out var found) || !found.GetBoolean()) return [];
        var rows = new List<ReadRow>();
        foreach (var r in doc.RootElement.GetProperty("rows").EnumerateArray())
        {
            var odds = r.GetProperty("odds").GetDecimal();
            if (odds < 1.0m) continue; // not a price
            var interval = r.GetProperty("interval").ValueKind == JsonValueKind.String ? r.GetProperty("interval").GetString() : null;
            rows.Add(new ReadRow(r.GetProperty("side").GetString()!, r.GetProperty("line").GetDecimal(), Math.Round(odds, 3), interval?.Replace(" ", "")));
        }
        return rows;
    }
}
