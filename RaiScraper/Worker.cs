using Microsoft.Extensions.Options;
using PuppeteerSharp;
using RaiScraper.Helpers;
using RaiScraper.Models;
using RaiScraper.Services;
using System.Collections.Concurrent;

namespace RaiScraper
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IScraperService _scraperService;
        private readonly IBrowserService _browserService;
        private readonly IUrlService _urlService;
        private readonly IModelProcessingService _modelProcessingService;
        private readonly List<string>? _sourceUrls;
        private readonly AppSettingOptions _appSettings;
        private bool _isParsing;

        public Worker(ILogger<Worker> logger,
                      IOptions<AppSettingOptions> appSettingsOptions,
                      IScraperService scraperService,
                      IBrowserService browserService,
                      IUrlService urlService,
                      IModelProcessingService modelProcessingService)
        {
            if (appSettingsOptions is null)
            {
                throw new ArgumentNullException(nameof(appSettingsOptions));
            }

            _logger = logger;
            _scraperService = scraperService;
            _browserService = browserService;
            _urlService = urlService;
            _modelProcessingService = modelProcessingService;
            _appSettings = appSettingsOptions.Value;
            _sourceUrls = _appSettings.UrlAddressesToParse;



#if DEBUG
            _appSettings.OutputFolderPath = "C:\\Users\\marek\\Downloads\\mp3";
            _appSettings.LogFilePath = "C:\\Users\\marek\\Downloads\\Logs\\log-.txt";
            _appSettings.DownloadInfoPath = "C:\\Users\\marek\\Downloads\\Logs\\DownloadedUrls.txt";
            _appSettings.FFmpegPath = "C:\\Program Files\\Ffmpeg\\bin\\ffmpeg.exe";
#endif
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var semaphore = new SemaphoreSlim(_appSettings.MaxConcurrentDownloads);
            _urlService.InitializeDownloadedUrls();
            while (!stoppingToken.IsCancellationRequested)
            {
                if (ShouldStartParsing())
                {
                    try
                    {
                        LogStartParsing();
                        var reiNewsUrl = await ProcessSourceUrls(stoppingToken);
                        if (reiNewsUrl.Count < 1)
                        {
                            await HandleNoUrlForParsing(stoppingToken);
                            continue;
                        }
                        await ProcessRaiNewsUrls(reiNewsUrl, stoppingToken, semaphore);
                    }
                    catch (Exception ex)
                    {
                        LogInitializationError(ex);
                    }
                }
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            LogCompletionInfo();
            await Task.Delay(TimeSpan.FromHours(6), stoppingToken);
        }
        private async Task<List<RaiNewsModel>> ProcessSourceUrls(CancellationToken stoppingToken)
        {
            List<RaiNewsModel> reiNewsUrl = new();
            int urlCounter = 1;
            if (_sourceUrls is not null && _sourceUrls.Count > 0)
            {
                foreach (var sourceUrl in _sourceUrls)
                {
                    LogInitialParseInfo(urlCounter);
                    var parsedUrls = await _scraperService.ScrapeSourceUrlsToHtmlAsync(sourceUrl);
                    if (parsedUrls is null)
                    {
                        await HandleNoUrlForParsing(stoppingToken);
                        continue;
                    }
                    reiNewsUrl = await HandleParsedUrls(parsedUrls);
                    _logger.LogInformation("Finished downloading in url no: {counter}.", urlCounter);
                    urlCounter++;
                }
            }
            else
            {
                _isParsing = false;
            }
            return reiNewsUrl;
        }
        private async Task ProcessUrlGroupAsync(List<string> urlGroup, SemaphoreSlim semaphore, ConcurrentBag<RaiNewsModel> modelList)
        {
            await semaphore.WaitAsync();
            IPage? page = null;
            try
            {
                using var browser = await _browserService.LaunchBrowserAsync();
                foreach (var url in urlGroup)
                {
                    try
                    {
                        if (_urlService.IsFileAlreadyDownloaded(url))
                        {
                            continue;
                        }

                        page = await browser.NewPageAsync();
                        var model = await _modelProcessingService.CreateModelFromUrlAsync(url, page);

                        if (model != null && (!string.IsNullOrEmpty(model.Mp3Url) || !string.IsNullOrEmpty(model.Mp4Url)))
                        {
                            modelList.Add(model);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogCritical("An error occurred: {error}", ex.Message);
                        // optionally throw the error after logging: throw;
                    }
                    finally
                    {
                        if (page != null) // If page was assigned
                        {
                            await page.CloseAsync();
                        }
                    }
                }

            }
            finally
            {
                semaphore.Release();
            }
        }
        private async Task<List<RaiNewsModel>> ConvertUrlsToModel(List<string> playerUrls)
        {
            _logger.LogInformation("Let's convert string player urls to the object models.");

            var modelList = new ConcurrentBag<RaiNewsModel>();
            var urlGroups = _urlService.GetUrlGroups(playerUrls);

            // SemaphoreSlim is used to limit concurrent threads.
            var semaphore = new SemaphoreSlim(_appSettings.MaxConcurrentDownloads);

            var tasks = urlGroups.Select(urlGroup => ProcessUrlGroupAsync(urlGroup, semaphore, modelList));

            await Task.WhenAll(tasks);

            return modelList.ToList();
        }
        #region Helping methods
        private bool ShouldStartParsing()
        {
            var now = DateTime.Now;
            // 100 means anytime
            return _appSettings.ParsingHours is null || _appSettings.ParsingHours.Contains(100) || _appSettings.ParsingHours.Contains(now.Hour) && !_isParsing;
        }
        private void LogStartParsing()
        {
            _isParsing = true;
            _logger.LogInformation("Worker Mp3 Scraper running at: {time}", DateTimeOffset.Now);
        }
        private void LogInitialParseInfo(int counter)
        {
            var totalUrls = _sourceUrls?.Count ?? 0;
            _logger.LogInformation("Ready to parse total urls: {url}", totalUrls);
            _logger.LogInformation("Start parsing url ------------ {counter}. ----------- at {date}", counter, DateTime.Now);
        }
        private async Task<List<RaiNewsModel>> HandleParsedUrls(List<string> parsedUrls)
        {
            _logger.LogInformation("System found total url with player: {url}", parsedUrls.Count);
            var reiNewsUrl = await ConvertUrlsToModel(parsedUrls);
            if (parsedUrls?.Count != reiNewsUrl.Count)
            {
                _logger.LogInformation("After conversion and checking if any file has already been downloaded, we are left with a total of: {count}", reiNewsUrl.Count);
            }
            return reiNewsUrl;
        }
        private async Task HandleNoUrlForParsing(CancellationToken stoppingToken)
        {
            _logger.LogWarning("No new Url for parsing. Will wait 1 hour.");
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
        private async Task ProcessRaiNewsUrls(List<RaiNewsModel> reiNewsUrl, CancellationToken stoppingToken, SemaphoreSlim semaphore)
        {
            foreach (var rnModel in reiNewsUrl)
            {
                if (_modelProcessingService.IsValidReiNewsModel(rnModel))
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        await _modelProcessingService.ProcessIndividualReiNewsModel(rnModel, stoppingToken);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }
                else
                {
                    Console.WriteLine($"SourceUrl doesn't contain any media file.");
                }
            }
        }
        private void LogInitializationError(Exception ex)
        {
            _logger.LogCritical("Error when trying to initialize the browser", ex);
            throw new Exception("Error when trying to initialize the browser", ex);
        }
        private void LogCompletionInfo()
        {
            _logger.LogInformation("Done. ---------------------- Parsing all XML urls has finished at {DateTime.Now}.", DateTime.Now);
        }
        #endregion
    }

}