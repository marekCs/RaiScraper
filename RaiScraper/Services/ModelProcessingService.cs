using Microsoft.Extensions.Options;
using PuppeteerSharp;
using RaiScraper.Helpers;
using RaiScraper.Models;

namespace RaiScraper.Services
{
    public class ModelProcessingService : IModelProcessingService
    {
        private readonly Random _random = new();
        private readonly ILogger<ModelProcessingService> _logger;
        private readonly IDownloadService _downloadService;
        private readonly IUrlService _urlService;
        private readonly IBrowserService _browserService;
        private readonly AppSettingOptions _appSettings;
        private readonly HashSet<string> _downloadedUrls;

        public ModelProcessingService(ILogger<ModelProcessingService> logger,
                                      IOptions<AppSettingOptions> appSettings,
                                      IDownloadService downloadService,
                                      IUrlService urlService,
                                      IBrowserService browserService)
        {
            _logger = logger;
            _downloadService = downloadService;
            _urlService = urlService;
            _browserService = browserService;
            _appSettings = appSettings.Value ?? throw new ArgumentNullException(nameof(appSettings));
            _downloadedUrls = new HashSet<string>();

        }
        public bool IsValidReiNewsModel(RaiNewsModel rnModel)
        {
            return rnModel is not null && !string.IsNullOrEmpty(rnModel.SourceUrl) && rnModel != null && (!string.IsNullOrEmpty(rnModel.Mp3Url) || !string.IsNullOrEmpty(rnModel.Mp4Url)) && _appSettings.DownloadInfoPath is not null;
        }
        public async Task ProcessIndividualReiNewsModel(RaiNewsModel rnModel, CancellationToken stoppingToken)
        {
            if (rnModel == null) throw new ArgumentNullException(nameof(rnModel));
            if (rnModel.SourceUrl == null) throw new ArgumentException($"{nameof(rnModel.SourceUrl)} cannot be null.", nameof(rnModel));
            if (_appSettings.DownloadInfoPath == null) throw new ArgumentException($"{nameof(_appSettings.DownloadInfoPath)} cannot be null.", nameof(_appSettings));

            await _downloadService.DownloadMedium(rnModel);
            _downloadedUrls.Add(rnModel.SourceUrl);
            await File.AppendAllTextAsync(_appSettings.DownloadInfoPath, rnModel.SourceUrl + "\n", stoppingToken);
        }
        public async Task<RaiNewsModel> CreateModelFromUrlAsync(string url, IPage page)
        {
            string domain = new Uri(url).Host;
            var model = new RaiNewsModel();
            var navigationOptions = new NavigationOptions
            {
                Timeout = 0,
                WaitUntil = new[] { WaitUntilNavigation.DOMContentLoaded }
            };
            try
            {
                await page.SetUserAgentAsync(_browserService.GetRandomUserAgent());
                await page.GoToAsync(url, navigationOptions);
                await Task.Delay(_random.Next(280, 450));
                await page.EvaluateFunctionAsync("() => { window.scrollTo(0, 400);}");
                var mediumUrls = new HashSet<string>();

                page.Request += (sender, e) =>
                {
                    // Ověřte, zda je URL požadavku URL videa nebo audio
                    if (e.Request.Url.Contains(".mp4") || e.Request.Url.Contains(".mp3") || e.Request.Url.Contains(".m3u8"))
                    {
                        mediumUrls.Add(e.Request.Url);
                    }
                };
                await Task.Delay(_random.Next(_appSettings.RandomValueFrom, _appSettings.RandomValueTo));
                var urlParts = url.Split('/');
                bool isItRaiPlaySound = domain.Contains("raiplaysound");
                model.SourceUrl = url;
                model.Region = isItRaiPlaySound ? urlParts[6].Split('-')[1] : urlParts[4];
                model.Channel = isItRaiPlaySound ? urlParts[6].Split('-')[0] : urlParts[3];

                var (year, month, day, hour, minute) = await _urlService.GetDateAndTimeAsync(urlParts, page, domain);

                var videoElement = await page.QuerySelectorAsync("#vjs_video_3_THEOplayerAqt video");
                var titleHelper = _urlService.CreateTitleIdentification(urlParts, domain);
                var titleElement = await page.QuerySelectorAsync("h1");



                if (mediumUrls.Count > 0 || videoElement != null)
                {
                    if (mediumUrls.Count > 0)
                    {
                        _logger.LogInformation("Media Url was found in the source {url}.", url);

                        foreach (var mediumUrl in mediumUrls)
                        {
                            if (mediumUrl.Contains(".mp3"))
                            {
                                model.Mp3Url = mediumUrl;
                            }
                            else
                            {
                                model.Mp4Url = mediumUrl;
                            }
                        }
                    }

                    var titleHandle = await titleElement.GetPropertyAsync("textContent");
                    var tryTitle = await titleHandle.JsonValueAsync<string>();
                    var srcHandle = await videoElement.GetPropertyAsync("src");
                    var mp3Url = await srcHandle.JsonValueAsync<string>();
                    if (model.Mp3Url is null && mp3Url.Length > 0 && mp3Url.Contains(".mp3"))
                    {
                        model.Mp3Url = mp3Url;
                    }

                    model.Title = tryTitle ?? titleHelper;
                    model.Year = year;
                    model.Month = month;
                    model.Day = day;
                    model.Hour = hour;
                    model.Minute = minute;

                    if ((!string.IsNullOrEmpty(model.Mp3Url) && model.Mp3Url.Contains(".mp3")) || (!string.IsNullOrEmpty(model.Mp4Url) && model.Mp4Url.Contains(".m3u8")))
                    {
                        _logger.LogInformation("Successfully mapped. Mp3 url: {mp3Url} or Mp4 url: {mp4Url}", model.Mp3Url, model.Mp4Url);
                        return model;
                    }
                    else
                    {
                        _logger.LogInformation("Failed to map medium in url: {url}.", url);
                        return model;
                    }
                }
                return model;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error on downloading/converting: {ex.Message}");
                return model;
            }
            finally
            {
                await page.CloseAsync();
                await page.DisposeAsync();
            }
        }
    }
}
