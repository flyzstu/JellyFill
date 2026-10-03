using MediaBrowser.Model.Plugins;

namespace JellyFill.Configuration;

/// <summary>
/// Plugin configuration for JellyFill.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        ServerUrl = "http://127.0.0.1:8089";
        ApiToken = string.Empty;
        DefaultProvider = "JAV321";
        EnablePosterCrop = true;
        EnableActors = true;
    }

    /// <summary>
    /// Gets or sets the JellyFill server URL.
    /// </summary>
    public string ServerUrl { get; set; }

    /// <summary>
    /// Gets or sets the optional API token for authentication.
    /// </summary>
    public string ApiToken { get; set; }

    /// <summary>
    /// Gets or sets the default metadata provider.
    /// </summary>
    public string DefaultProvider { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to request smart 2:3 cropped posters from server.
    /// </summary>
    public bool EnablePosterCrop { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to populate actresses/actors.
    /// </summary>
    public bool EnableActors { get; set; }
}
