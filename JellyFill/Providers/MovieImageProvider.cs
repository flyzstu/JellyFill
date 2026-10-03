using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using JellyFill.Common;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace JellyFill.Providers;

/// <summary>
/// Image provider for Jellyfin movies.
/// </summary>
public class MovieImageProvider : IRemoteImageProvider, IHasOrder
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<MovieImageProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MovieImageProvider"/> class.
    /// </summary>
    public MovieImageProvider(IHttpClientFactory httpClientFactory, ILogger<MovieImageProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "JellyFill";

    /// <inheritdoc />
    public int Order => 1;

    /// <inheritdoc />
    public bool Supports(BaseItem item) => item is Movie;

    /// <inheritdoc />
    public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
    {
        return new[] { ImageType.Primary, ImageType.Backdrop, ImageType.Thumb };
    }

    /// <inheritdoc />
    public Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
    {
        var images = new List<RemoteImageInfo>();

        string? provider = item.GetProviderId("JellyFill-Provider");
        string? id = item.GetProviderId("JellyFill-Id");

        if (string.IsNullOrEmpty(provider) || string.IsNullOrEmpty(id))
        {
            return Task.FromResult<IEnumerable<RemoteImageInfo>>(images);
        }

        string serverUrl = Plugin.Instance?.Configuration.ServerUrl.TrimEnd('/') ?? "http://127.0.0.1:8089";
        string primaryUrl = $"{serverUrl}/v1/images/primary/{provider}/{id}";
        string backdropUrl = $"{serverUrl}/v1/images/backdrop/{provider}/{id}";
        string thumbUrl = $"{serverUrl}/v1/images/thumb/{provider}/{id}";

        // 1. Primary Poster: Smart 2:3 vertical crop from JellyFill-Server
        images.Add(new RemoteImageInfo
        {
            Type = ImageType.Primary,
            ProviderName = Name,
            Url = primaryUrl
        });

        // 2. Backdrop / Fanart: Wide full cover
        images.Add(new RemoteImageInfo
        {
            Type = ImageType.Backdrop,
            ProviderName = Name,
            Url = backdropUrl
        });

        // 3. Thumb / Landscape: Wide sample screenshot from JellyFill-Server
        images.Add(new RemoteImageInfo
        {
            Type = ImageType.Thumb,
            ProviderName = Name,
            Url = thumbUrl
        });

        PluginLogStore.Add(
            PluginLogLevel.Info,
            "MovieImageProvider",
            $"提供图片下载源 [{provider}:{id}]: 竖版海报(Primary)、全景背景(Backdrop)与横版缩略图(Thumb)");

        return Task.FromResult<IEnumerable<RemoteImageInfo>>(images);
    }

    /// <inheritdoc />
    public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient();
        if (!string.IsNullOrEmpty(Plugin.Instance?.Configuration.ApiToken))
        {
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {Plugin.Instance.Configuration.ApiToken}");
        }

        PluginLogStore.Add(PluginLogLevel.Debug, "MovieImageProvider", $"正在拉取图片数据: {url}");
        var response = await client.GetAsync(new Uri(url), cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            PluginLogStore.Add(PluginLogLevel.Warning, "MovieImageProvider", $"拉取图片响应异常 HTTP {(int)response.StatusCode}: {url}");
        }

        return response;
    }
}
