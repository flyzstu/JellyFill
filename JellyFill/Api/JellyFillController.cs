using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using JellyFill.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace JellyFill.Api;

/// <summary>
/// Request model for filename parsing debug.
/// </summary>
public class ParseFilenameRequest
{
    /// <summary>
    /// Filename to parse.
    /// </summary>
    public string Filename { get; set; } = string.Empty;
}

/// <summary>
/// JellyFill Web Dashboard API Controller.
/// Provides live log inspection and scraper debugging endpoints for Jellyfin Web UI.
/// </summary>
[ApiController]
[Route("JellyFill")]
[Produces(MediaTypeNames.Application.Json)]
public class JellyFillController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyFillController"/> class.
    /// </summary>
    public JellyFillController(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Gets all buffered plugin logs.
    /// </summary>
    /// <returns>A list of log entries ordered newest first.</returns>
    [HttpGet("Logs")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult GetLogs()
    {
        // Jellyfin API 默认按 PascalCase 序列化，而配置页 JS 按 camelCase 读取，
        // 这里显式投影成 camelCase，避免日志页时间/正文显示为空。
        var entries = PluginLogStore.GetLogs();
        var logs = new List<object>(entries.Count);
        foreach (var e in entries)
        {
            logs.Add(new
            {
                timestamp = e.Timestamp,
                level = e.Level.ToString(),
                source = e.Source,
                message = e.Message,
                details = e.Details
            });
        }

        return Ok(logs);
    }

    /// <summary>
    /// Clears the in-memory log buffer.
    /// </summary>
    /// <returns>Status response.</returns>
    [HttpPost("Logs/Clear")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult ClearLogs()
    {
        PluginLogStore.Clear();
        return Ok(new { success = true, message = "日志已清空" });
    }

    /// <summary>
    /// Tests filename parsing logic against regex rules.
    /// </summary>
    /// <param name="request">The parse request containing raw filename.</param>
    /// <returns>The extracted 番号 and original filename.</returns>
    [HttpPost("Debug/Parse")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult Parse([FromBody] ParseFilenameRequest request)
    {
        var filename = request?.Filename ?? string.Empty;
        var number = FilenameParser.Parse(filename);

        PluginLogStore.Add(
            string.IsNullOrEmpty(number) ? PluginLogLevel.Warning : PluginLogLevel.Info,
            "Debugger",
            $"调试解析测试: '{filename}' -> '{(string.IsNullOrEmpty(number) ? "未匹配" : number)}'");

        return Ok(new
        {
            filename,
            number,
            success = !string.IsNullOrEmpty(number)
        });
    }

    /// <summary>
    /// Tests full scraping flow directly from Jellyfin Web console.
    /// </summary>
    /// <param name="q">Movie number or search keyword.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Search results from the JellyFill backend server.</returns>
    [HttpGet("Debug/Search")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult> DebugSearch([FromQuery] string q, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return BadRequest(new { error = "查询参数 q 不能为空" });
        }

        var number = FilenameParser.Parse(q);
        var query = string.IsNullOrEmpty(number) ? q.Trim() : number;

        var serverUrl = Plugin.Instance?.Configuration.ServerUrl.TrimEnd('/') ?? "http://127.0.0.1:8089";
        var url = $"{serverUrl}/v1/movies/search?q={Uri.EscapeDataString(query)}";

        PluginLogStore.Add(PluginLogLevel.Info, "Debugger", $"[调试抓取] 开始搜索: 番号={query}, URL={url}");

        try
        {
            var client = _httpClientFactory.CreateClient();
            if (!string.IsNullOrEmpty(Plugin.Instance?.Configuration.ApiToken))
            {
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {Plugin.Instance.Configuration.ApiToken}");
            }

            var resp = await client.GetFromJsonAsync<ApiResponse<List<MovieSearchResult>>>(url, cancellationToken).ConfigureAwait(false);
            var count = resp?.Data?.Count ?? 0;

            PluginLogStore.Add(
                count > 0 ? PluginLogLevel.Success : PluginLogLevel.Warning,
                "Debugger",
                $"[调试抓取] 搜索完成，找到 {count} 条结果");

            return Ok(new
            {
                query,
                serverUrl,
                count,
                results = resp?.Data ?? new List<MovieSearchResult>()
            });
        }
        catch (Exception ex)
        {
            PluginLogStore.Add(PluginLogLevel.Error, "Debugger", $"[调试抓取] 搜索异常: {ex.Message}");
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                error = ex.Message,
                serverUrl,
                query
            });
        }
    }
}
