using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using JellyFill.Common;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace JellyFill.Providers;

/// <summary>
/// Movie metadata provider for Jellyfin.
/// </summary>
public class MovieProvider : IRemoteMetadataProvider<Movie, MovieInfo>, IHasOrder
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<MovieProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MovieProvider"/> class.
    /// </summary>
    public MovieProvider(IHttpClientFactory httpClientFactory, ILogger<MovieProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "JellyFill";

    /// <inheritdoc />
    public int Order => 1;

    /// <inheritdoc />
    public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(MovieInfo info, CancellationToken cancellationToken)
    {
        var results = new List<RemoteSearchResult>();

        string rawName = Path.GetFileNameWithoutExtension(string.IsNullOrEmpty(info.Path) ? info.Name : info.Path);
        string? canonicalNumber = FilenameParser.ExtractCanonicalNumber(rawName);

        // Fallback: If filename did not yield a canonical 番号, inspect parent directory name
        if (string.IsNullOrEmpty(canonicalNumber) && !string.IsNullOrEmpty(info.Path))
        {
            string? parentDir = Path.GetFileName(Path.GetDirectoryName(info.Path));
            if (!string.IsNullOrEmpty(parentDir))
            {
                canonicalNumber = FilenameParser.ExtractCanonicalNumber(parentDir);
                if (!string.IsNullOrEmpty(canonicalNumber))
                {
                    PluginLogStore.Add(PluginLogLevel.Info, "MovieProvider", $"[搜索] 从父文件夹识别到番号 [{canonicalNumber}] (路径: '{info.Path}')");
                }
            }
        }

        string number = canonicalNumber ?? FilenameParser.Parse(rawName);

        if (string.IsNullOrEmpty(number))
        {
            _logger.LogDebug("[JellyFill] Could not parse number from filename: {RawName}", rawName);
            PluginLogStore.Add(PluginLogLevel.Warning, "MovieProvider", $"[搜索] 无法识别有效番号: '{rawName}'");
            return results;
        }

        PluginLogStore.Add(PluginLogLevel.Info, "MovieProvider", $"[搜索] 识别到番号 [{number}] (来源: '{rawName}')");

        string serverUrl = Plugin.Instance?.Configuration.ServerUrl.TrimEnd('/') ?? "http://127.0.0.1:8089";
        string url = $"{serverUrl}/v1/movies/search?q={Uri.EscapeDataString(number)}";

        try
        {
            var client = _httpClientFactory.CreateClient();
            if (!string.IsNullOrEmpty(Plugin.Instance?.Configuration.ApiToken))
            {
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {Plugin.Instance.Configuration.ApiToken}");
            }

            var resp = await client.GetFromJsonAsync<ApiResponse<List<MovieSearchResult>>>(url, cancellationToken).ConfigureAwait(false);
            if (resp?.Data != null && resp.Data.Count > 0)
            {
                PluginLogStore.Add(PluginLogLevel.Info, "MovieProvider", $"[搜索] 番号 [{number}] 服务端检索成功，匹配到 {resp.Data.Count} 条记录");
                foreach (var item in resp.Data)
                {
                    if (string.IsNullOrEmpty(item.Id))
                    {
                        continue;
                    }

                    // 候选一（默认）：横版封套右侧裁切
                    var cropped = new RemoteSearchResult
                    {
                        Name = $"{item.Title}（封面裁切）",
                        SearchProviderName = Name,
                        // ThumbUrl 优先（JAV321 为竖版 ps 封面），避免识别弹窗 2:3 框中心裁切横版封套
                        ImageUrl = item.ThumbUrl ?? item.CoverUrl
                    };

                    cropped.SetProviderId("JellyFill-Provider", item.Provider);
                    cropped.SetProviderId("JellyFill-Id", item.Id);

                    DateTime? premiere = DateTime.TryParse(item.ReleaseDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : null;
                    if (premiere.HasValue)
                    {
                        cropped.PremiereDate = premiere;
                        cropped.ProductionYear = premiere.Value.Year;
                    }

                    results.Add(cropped);

                    // 候选二：完整竖版封面（服务端 404 = 无高清竖版，不提供该候选）
                    var verticalUrl = $"{serverUrl}/v1/images/primary/{item.Provider}/{Uri.EscapeDataString(item.Id)}?mode=vertical";
                    if (await ProbeUrlAsync(verticalUrl, cancellationToken).ConfigureAwait(false))
                    {
                        var vertical = new RemoteSearchResult
                        {
                            Name = $"{item.Title}（完整竖版）",
                            SearchProviderName = Name,
                            ImageUrl = cropped.ImageUrl
                        };
                        vertical.SetProviderId("JellyFill-Provider", item.Provider);
                        vertical.SetProviderId("JellyFill-Id", item.Id);
                        vertical.SetProviderId("JellyFill-PosterMode", "vertical");
                        if (premiere.HasValue)
                        {
                            vertical.PremiereDate = premiere;
                            vertical.ProductionYear = premiere.Value.Year;
                        }
                        results.Add(vertical);

                        PluginLogStore.Add(PluginLogLevel.Info, "MovieProvider", $"[搜索] [{item.Number}] 提供双封面候选：封面裁切（默认）+ 完整竖版");
                    }
                }
            }
            else
            {
                PluginLogStore.Add(PluginLogLevel.Warning, "MovieProvider", $"[搜索] 番号 [{number}] 服务端未检索到有效结果");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[JellyFill] Failed to query search for {Number}", number);
            PluginLogStore.Add(PluginLogLevel.Error, "MovieProvider", $"[搜索] 番号 [{number}] 请求异常: {ex.Message}");
        }

        return results;
    }

    /// <inheritdoc />
    public async Task<MetadataResult<Movie>> GetMetadata(MovieInfo info, CancellationToken cancellationToken)
    {
        var result = new MetadataResult<Movie> { HasMetadata = false };

        string? provider = info.GetProviderId("JellyFill-Provider") ?? Plugin.Instance?.Configuration.DefaultProvider ?? "JAV321";
        string? id = info.GetProviderId("JellyFill-Id");
        string? posterMode = info.GetProviderId("JellyFill-PosterMode");

        // If ID is missing, try identifying from search
        if (string.IsNullOrEmpty(id))
        {
            var searchResults = await GetSearchResults(info, cancellationToken).ConfigureAwait(false);
            var first = searchResults.FirstOrDefault();
            if (first == null)
            {
                return result;
            }

            provider = first.GetProviderId("JellyFill-Provider") ?? provider;
            id = first.GetProviderId("JellyFill-Id");
            posterMode = first.GetProviderId("JellyFill-PosterMode");
            if (string.IsNullOrEmpty(id))
            {
                return result;
            }
        }

        PluginLogStore.Add(PluginLogLevel.Info, "MovieProvider", $"[元数据] 开始拉取详情: [{provider}] {id}");

        string serverUrl = Plugin.Instance?.Configuration.ServerUrl.TrimEnd('/') ?? "http://127.0.0.1:8089";
        string url = $"{serverUrl}/v1/movies/{provider}/{id}";

        try
        {
            var client = _httpClientFactory.CreateClient();
            if (!string.IsNullOrEmpty(Plugin.Instance?.Configuration.ApiToken))
            {
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {Plugin.Instance.Configuration.ApiToken}");
            }

            var resp = await client.GetFromJsonAsync<ApiResponse<MovieDetail>>(url, cancellationToken).ConfigureAwait(false);
            if (resp?.Data != null)
            {
                var data = resp.Data;
                var translatedTitle = string.IsNullOrWhiteSpace(data.TitleTranslated) ? data.Title : data.TitleTranslated;
                var displayTitle = string.IsNullOrEmpty(data.Number) ? translatedTitle : $"{data.Number} {translatedTitle}";

                var movie = new Movie
                {
                    // 主标题 = 番号 + 标题翻译；原始标题(副标题) = 原文
                    Name = displayTitle,
                    OriginalTitle = data.Title,
                    Overview = data.Summary,
                    CommunityRating = (float?)data.Score
                };

                // 结构化信息以 NFO tag 形式写入，不放进正文
                if (data.Tags != null && data.Tags.Count > 0)
                {
                    movie.Tags = data.Tags.ToArray();
                }

                if (DateTime.TryParse(data.ReleaseDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                {
                    movie.PremiereDate = dt;
                    movie.ProductionYear = dt.Year;
                }

                if (data.Runtime.HasValue && data.Runtime.Value > 0)
                {
                    movie.RunTimeTicks = TimeSpan.FromMinutes(data.Runtime.Value).Ticks;
                }

                if (!string.IsNullOrEmpty(data.Maker))
                {
                    movie.AddStudio(data.Maker);
                }

                if (data.Genres != null)
                {
                    foreach (var genre in data.Genres)
                    {
                        movie.AddGenre(genre);
                    }
                }

                if (Plugin.Instance?.Configuration.EnableActors != false && data.Actors != null)
                {
                    foreach (var actor in data.Actors)
                    {
                        result.AddPerson(new PersonInfo
                        {
                            Name = actor,
                            Type = PersonKind.Actor
                        });
                    }
                }

                if (!string.IsNullOrEmpty(data.Director))
                {
                    result.AddPerson(new PersonInfo
                    {
                        Name = data.Director,
                        Type = PersonKind.Director
                    });
                }

                movie.SetProviderId("JellyFill-Provider", provider);
                movie.SetProviderId("JellyFill-Id", id);
                // 记住用户在识别弹窗选择的封面模式（vertical = 完整竖版，缺省 = 封面裁切）
                if (!string.IsNullOrEmpty(posterMode))
                {
                    movie.SetProviderId("JellyFill-PosterMode", posterMode);
                }

                result.Item = movie;
                result.HasMetadata = true;

                PluginLogStore.Add(
                    PluginLogLevel.Success,
                    "MovieProvider",
                    $"[元数据] 成功绑定元数据: [{data.Number}] {data.Title}, 制作商: {data.Maker ?? "未知"}, 演员: {(data.Actors != null && data.Actors.Count > 0 ? string.Join(", ", data.Actors) : "无")}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[JellyFill] Failed to retrieve metadata for {Provider}/{Id}", provider, id);
            PluginLogStore.Add(PluginLogLevel.Error, "MovieProvider", $"[元数据] 获取异常 [{provider}:{id}]: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// 探测 URL 是否可用（服务端对「无高清竖版候选」返回 404）。
    /// </summary>
    private async Task<bool> ProbeUrlAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Head, new Uri(url));
            if (!string.IsNullOrEmpty(Plugin.Instance?.Configuration.ApiToken))
            {
                request.Headers.Add("Authorization", $"Bearer {Plugin.Instance.Configuration.ApiToken}");
            }

            var resp = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            PluginLogStore.Add(PluginLogLevel.Warning, "MovieProvider", $"[搜索] 竖版候选探测失败: {ex.Message}");
            return false;
        }
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient();
        return client.GetAsync(new Uri(url), cancellationToken);
    }
}
