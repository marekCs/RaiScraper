using Microsoft.Extensions.Options;
using RaiScraper.Helpers;
using RaiScraper.Models;
using System.Diagnostics;

namespace RaiScraper.Services
{
    public class DownloadService : IDownloadService
    {
        private readonly ILogger<DownloadService> _logger;
        private readonly IUrlService _urlService;
        private readonly AppSettingOptions _appSettings;

        public DownloadService(ILogger<DownloadService> logger, IUrlService urlService, IOptions<AppSettingOptions> appSettings)
        {
            _logger = logger;
            _urlService = urlService;
            _appSettings = appSettings.Value ?? throw new ArgumentNullException(nameof(appSettings));
        }
        public async Task DownloadMedium(RaiNewsModel model)
        {
            try
            {
                string path = _urlService.PreparePath(model);

                // Check if file exists
                if (File.Exists(path))
                {
                    Console.WriteLine($"File already exists: {path}");
                    return;
                }
                if (model.Mp3Url is not null && model.Mp3Url.Length > 0 && model.Mp3Url.Contains(".mp3"))
                {
                    HttpClient _httpClient = new();
                    var bytes = await _httpClient.GetByteArrayAsync(model.Mp3Url);
                    await File.WriteAllBytesAsync(path, bytes);
                    _logger.LogInformation("Mp3 has been successfully downloaded: {path}", path);
                }
                else if (model.Mp4Url is not null && model.Mp4Url.Length > 0)
                {
                    await DownloadVideoToAudio(model.Mp4Url, path);
                }

            }
            catch (Exception ex)
            {
                _logger.LogError("An error occurred while downloading a media file. {message}", ex.Message);
            }
        }

        private async Task DownloadVideoToAudio(string url, string path)
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _appSettings.FFmpegPath,
                    Arguments = $"-i \"{url}\" -vn -b:a 128k \"{path}\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }
            };
            process.Start();
            await process.WaitForExitAsync();
        }
    }
}
