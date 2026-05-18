using System.Text.Json.Serialization;

namespace soundCloudArchiver.Models;

public class AppState
{
    public bool IsInitialSetupComplete { get; set; } = false;

    [JsonPropertyName("download_path")]
    public string DownloadPath { get; set; } = "";
}
