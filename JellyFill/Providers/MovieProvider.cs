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
                PluginLogStore.Add(PluginLogLevel.Success, "MovieProvider", $"[搜索] 番号 [{number}] 服务端检索成功，匹配到 {resp.Data.Count} 条记录");
                foreach (var item in resp.Data)
                {
                    var result = new RemoteSearchResult
                    {
                        Name = item.Title,
                        SearchProviderName = Name,
                        ImageUrl = item.CoverUrl ?? item.ThumbUrl
                    };

                    result.SetProviderId("JellyFill-Provider", item.Provider);
                    result.SetProviderId("JellyFill-Id", item.Id);

                    if (DateTime.TryParse(item.ReleaseDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                    {
                        result.PremiereDate = dt;
                        result.ProductionYear = dt.Year;
                    }

                    results.Add(result);
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
                var movie = new Movie
                {
                    Name = data.Title,
                    OriginalTitle = data.Number,
                    Overview = data.Summary,
                    CommunityRating = (float?)data.Score
                };

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

    /// <inheritdoc />
    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient();
        return client.GetAsync(new Uri(url), cancellationToken);
    }
}
