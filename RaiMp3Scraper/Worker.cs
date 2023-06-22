using HtmlAgilityPack;
using Microsoft.Extensions.Options;
using PuppeteerSharp;
using RaiMp3Scraper.Helpers;
using RaiMp3Scraper.Models;
using System.Collections.Concurrent;
using System.Globalization;
using System.Xml;

namespace RaiMp3Scraper
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private List<string>? _xmlSitemapUrls;
        private string? _outputFolderPath;
        private string? _urlBase;
        private DateTime _dateFrom;
        private DateTime _dateTo;
        private readonly AppSettings _appSettings;
        IBrowser? _browser = null;
        private BrowserFetcher _browserFetcher;
        private readonly HashSet<string> _downloadedUrls;
        private bool _isParsing;
        private const int _urlsPerBrowser = 20;
        private const string _browserArg1 = "--disable-web-security";
        private const string _browserArg2 = "--disable-features=IsolateOrigins,site-per-process";
        private readonly Random _random = new();
        public Worker(ILogger<Worker> logger, IOptions<AppSettings> appSettingsOptions)
        {
            _logger = logger;
            _appSettings = appSettingsOptions.Value;
            _downloadedUrls = new HashSet<string>();

            _browserFetcher = new BrowserFetcher();
            _browser = LaunchBrowserAsync().GetAwaiter().GetResult();

            AppDomain.CurrentDomain.ProcessExit += (sender, args) =>
            {
                EndParsing();
            };

#if DEBUG
            _appSettings.OutputFolderPath = "C:\\Users\\marek\\Downloads\\mp3";
            _appSettings.LogFilePath = "C:\\Users\\marek\\Downloads\\Logs\\log-.txt";
            _appSettings.DownloadInfoPath = "C:\\Users\\marek\\Downloads\\Logs\\DownloadedUrls.txt";
#endif
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var semaphore = new SemaphoreSlim(_appSettings.MaxConcurrentDownloads);
            InitializeDownloadedUrls();
            while (!stoppingToken.IsCancellationRequested)
            {
                if (ShouldStartParsing())
                {
                    try
                    {
                        StartParsing();
                        InitializeSettings();
                        var reiNewsUrl = await ProcessXmlUrls(stoppingToken);
                        if (reiNewsUrl.Count < 1)
                        {
                            await HandleNoUrlForParsing(stoppingToken);
                            continue;
                        }
                        await ProcessReiNewsUrls(reiNewsUrl, stoppingToken, semaphore);
                    }
                    catch (Exception ex)
                    {
                        HandleInitializationError(ex);
                    }
                    finally
                    {
                        EndParsing();
                    }
                }
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            LogCompletionInfo();
            await Task.Delay(TimeSpan.FromHours(6), stoppingToken);
        }
        private async Task<ReiNewsModel> CreateModelFromUrlAsync(string url, IPage page)
        {
            await page.GoToAsync(url, new NavigationOptions { Timeout = 60000 });
            await Task.Delay(_random.Next(80, 150));

            var urlParts = url.Split('/');
            var model = new ReiNewsModel
            {
                SourceUrl = url,
                Region = urlParts[4],
                Channel = urlParts[3]
            };

            var (year, month, day, hour, minute) = await GetDateAndTimeAsync(urlParts, page);

            var titleHelper = CreateTitleIdentification(urlParts);

            var videoElement = await page.QuerySelectorAsync("#vjs_video_3_THEOplayerAqt video");
            var titleElement = await page.QuerySelectorAsync("h1");

            if (videoElement != null)
            {
                var srcHandle = await videoElement.GetPropertyAsync("src");
                var titleHandle = await titleElement.GetPropertyAsync("textContent");
                var tryTitle = await titleHandle.JsonValueAsync<string>();

                model.Title = tryTitle ?? titleHelper;
                model.Mp3Url = await srcHandle.JsonValueAsync<string>();

                model.Year = year;
                model.Month = month;
                model.Day = day;
                model.Hour = hour;
                model.Minute = minute;

                if (!string.IsNullOrEmpty(model.Mp3Url) && model.Mp3Url.Contains(".mp3"))
                {
                    _logger.LogInformation("Successfully mapped. Mp3 url: {mp3Url}", model.Mp3Url);
                    return model;
                }
            }
            return new ReiNewsModel();
        }
        private async Task<List<ReiNewsModel>> ProcessXmlUrls(CancellationToken stoppingToken)
        {
            List<ReiNewsModel> reiNewsUrl = new List<ReiNewsModel>();
            int xmlUrlCounter = 1;
            if (_xmlSitemapUrls is not null && _xmlSitemapUrls.Count > 0)
            {
                foreach (var xmlUrl in _xmlSitemapUrls)
                {
                    LogXmlParseInfo(xmlUrlCounter);
                    var urlsFromXml = ParseXmlUrlsToHtml(_urlBase + xmlUrl);
                    if (urlsFromXml is null)
                    {
                        await HandleNoUrlForParsing(stoppingToken);
                        continue;
                    }
                    reiNewsUrl = await HandleUrlsFromXml(urlsFromXml);
                    _logger.LogInformation("Finished downloading in XML url no: {xmlUrlCounter}.", xmlUrlCounter);
                    xmlUrlCounter++;
                }
            }
            else
            {
                EndParsing();
            }
            return reiNewsUrl;
        }
        public List<string> ParseXmlUrlsToHtml(string sitemapUrl)
        {
            bool filterMustBeApplied = false;
            var xmlDoc = new XmlDocument();
            xmlDoc.Load(sitemapUrl);
            var urlNodes = xmlDoc.GetElementsByTagName("loc");

            _logger.LogInformation("This XML url contains in total {count} html web pages.", urlNodes.Count);
            // if there is a filter on the specific dates we want to parse
            if (_dateFrom != DateTime.MinValue && _dateTo != DateTime.MaxValue)
            {
                filterMustBeApplied = true;
                _logger.LogWarning("Looks like we'll have to apply the filter to the desired time period from {_dateFrom} to {_dateTo}.", _dateFrom, _dateTo);
            }

            // Filter URLs that contain "/audio/" or "/video/"
            var audioVideoUrls = urlNodes.Cast<XmlNode>()
                .Select(n => n.InnerText)
                .Where(url => url.Contains("/audio/") || url.Contains("/video/"))
                .ToList();

            if (!filterMustBeApplied)
            {
                return audioVideoUrls;
            }

            // If a filter is to be applied, we get the URLs that match the specified date
            var dateFilteredUrls = audioVideoUrls
                .Where(url =>
                {
                    // Get the part of the URL that contains the year and month
                    var yearMonthString = url.Split(new[] { "/" }, StringSplitOptions.None)
                        .SkipWhile(part => !int.TryParse(part, out _)) // Skip non-numeric parts
                        .Take(2) // Year, Month
                        .DefaultIfEmpty(string.Empty) // If there are not enough parts, use an empty string
                        .Aggregate((part1, part2) => $"{part1}-{part2}"); // Join the parts with hyphens

                    if (!DateTime.TryParseExact(yearMonthString, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                    {
                        return false; // If the date string could not be parsed, exclude the URL
                    }

                    date = new DateTime(date.Year, date.Month, 1); // Consider only year and month

                    var dateFrom = new DateTime(_dateFrom.Year, _dateFrom.Month, 1);
                    var dateTo = new DateTime(_dateTo.Year, _dateTo.Month, 1);

                    return date >= dateFrom && date <= dateTo;
                })
                .ToList();

            return dateFilteredUrls;
        }
        private async Task<List<ReiNewsModel>> ConvertUrlsToModel(List<string> playerUrls)
        {
            _logger.LogInformation("Let's convert string player urls to the object models.");

            var modelList = new ConcurrentBag<ReiNewsModel>();
            var urlGroups = GetUrlGroups(playerUrls);

            // SemaphoreSlim is used to limit concurrent threads.
            var semaphore = new SemaphoreSlim(_appSettings.MaxConcurrentDownloads);

            var tasks = urlGroups.Select(urlGroup => ProcessUrlGroupAsync(urlGroup, semaphore, modelList));

            await Task.WhenAll(tasks);

            return modelList.ToList();
        }
        private async Task ProcessUrlGroupAsync(List<string> urlGroup, SemaphoreSlim semaphore, ConcurrentBag<ReiNewsModel> modelList)
        {
            await semaphore.WaitAsync();

            try
            {
                _browser ??= await LaunchBrowserAsync();
                using var page = await _browser.NewPageAsync();

                await ConfigurePageAsync(page);

                foreach (var url in urlGroup)
                {
                    try
                    {
                        if (IsFileAlreadyDownloaded(url))
                        {
                            continue;
                        }

                        var model = await CreateModelFromUrlAsync(url, page);

                        if (model != null)
                        {
                            modelList.Add(model);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogCritical("An error occurred: {error}", ex.Message);
                        // optionally throw the error after logging: throw;
                    }
                }
                await page.CloseAsync();
            }
            finally
            {
                semaphore.Release();
            }
        }
        private async Task DownloadMp3(ReiNewsModel reiNewsModel)
        {
            try
            {
                string path = PreparePath(reiNewsModel);

                // Check if file exists
                if (File.Exists(path))
                {
                    Console.WriteLine($"File mp3 already exists: {path}");
                    return;
                }
                HttpClient _httpClient = new();
                var bytes = await _httpClient.GetByteArrayAsync(reiNewsModel.Mp3Url);
                await File.WriteAllBytesAsync(path, bytes);
                _logger.LogInformation("Mp3 has been successfully downloaded: {path}", path);
            }
            catch (Exception ex)
            {
                _logger.LogError("Error downloading {reiNewsModel.Mp3Url}: {ex.Message}", reiNewsModel.Mp3Url, ex.Message);
            }
        }

        #region Helping methods
        private void InitializeSettings()
        {
            if (_appSettings is null)
            {
                throw new NullReferenceException(nameof(_appSettings));
            }
            _outputFolderPath = _appSettings.OutputFolderPath;
            _xmlSitemapUrls = _appSettings.UrlAddressesToParse;
            _urlBase = _appSettings.UrlBase;
            _dateFrom = _appSettings.DateFrom;
            _dateTo = _appSettings.DateTo;
        }
        private void InitializeDownloadedUrls()
        {
            if (File.Exists(_appSettings.DownloadInfoPath))
            {
                var lines = File.ReadAllLines(_appSettings.DownloadInfoPath);
                foreach (var line in lines)
                {
                    _downloadedUrls.Add(line.Trim());
                }
            }
        }
        private bool ShouldStartParsing()
        {
            var now = DateTime.Now;
            // 100 means anytime
            return _appSettings.ParsingHours is null || _appSettings.ParsingHours.Contains(100) || _appSettings.ParsingHours.Contains(now.Hour) && !_isParsing;
        }
        private void StartParsing()
        {
            _isParsing = true;
            _logger.LogInformation("Worker Mp3 Scraper running at: {time}", DateTimeOffset.Now);
        }
        private void LogXmlParseInfo(int xmlUrlCounter)
        {
            var totalXMLUrlsToParse = _xmlSitemapUrls?.Count ?? 0;
            _logger.LogInformation("Ready to parse total XML urls: {url}", totalXMLUrlsToParse);
            _logger.LogInformation("Start parsing XML url ------------ {xmlUrlCounter}. ----------- at {date}", xmlUrlCounter, DateTime.Now);
        }
        private async Task<List<ReiNewsModel>> HandleUrlsFromXml(List<string> urlsFromXml)
        {
            _logger.LogInformation("System found total url with mp3 player: {url}", urlsFromXml.Count);
            var reiNewsUrl = await ConvertUrlsToModel(urlsFromXml);
            if (urlsFromXml?.Count != reiNewsUrl.Count)
            {
                _logger.LogInformation("After conversion and checking if any file has already been downloaded, we are left with a total of: {reiNewsUrl.Count}", reiNewsUrl.Count);
            }
            return reiNewsUrl;
        }
        private async Task HandleNoUrlForParsing(CancellationToken stoppingToken)
        {
            await DisposeAndReinitializeBrowserAsync();
            _logger.LogWarning("No new Url for parsing. Will wait 1 hour.");
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
        private async Task DisposeAndReinitializeBrowserAsync()
        {
            if (_browser != null)
            {
                await _browser.DisposeAsync();
                _browser = LaunchBrowserAsync().GetAwaiter().GetResult();
            }
        }
        private async Task ProcessReiNewsUrls(List<ReiNewsModel> reiNewsUrl, CancellationToken stoppingToken, SemaphoreSlim semaphore)
        {
            foreach (var rnModel in reiNewsUrl)
            {
                if (IsValidReiNewsModel(rnModel))
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        await ProcessIndividualReiNewsModel(rnModel, stoppingToken);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }
                else
                {
                    Console.WriteLine($"SourceUrl doesn't contain any mp3 file.");
                }
            }
        }
        private bool IsValidReiNewsModel(ReiNewsModel rnModel)
        {
            return rnModel is not null && !string.IsNullOrEmpty(rnModel.SourceUrl) && rnModel != null && (!string.IsNullOrEmpty(rnModel.Mp3Url)) && _appSettings.DownloadInfoPath is not null;
        }
        private async Task ProcessIndividualReiNewsModel(ReiNewsModel rnModel, CancellationToken stoppingToken)
        {
            if (rnModel == null) throw new ArgumentNullException(nameof(rnModel));
            if (rnModel.SourceUrl == null) throw new ArgumentNullException(nameof(rnModel.SourceUrl));
            if (_appSettings.DownloadInfoPath == null) throw new ArgumentNullException(nameof(_appSettings.DownloadInfoPath));

            await DownloadMp3(rnModel);
            _downloadedUrls.Add(rnModel.SourceUrl);
            await File.AppendAllTextAsync(_appSettings.DownloadInfoPath, rnModel.SourceUrl + "\n", stoppingToken);
        }
        private void HandleInitializationError(Exception ex)
        {
            _logger.LogCritical("Error when trying to initialize the browser", ex);
            throw new Exception("Error when trying to initialize the browser", ex);
        }
        private void EndParsing()
        {
            if (_browser != null)
            {
                _browser?.CloseAsync().GetAwaiter().GetResult();
                _browser?.DisposeAsync().GetAwaiter().GetResult();
                _browser = null;
            }
            _isParsing = false;

            _browserFetcher?.Dispose();
        }
        private void LogCompletionInfo()
        {
            _logger.LogInformation("Done. ---------------------- Parsing all XML urls has finished at {DateTime.Now}.", DateTime.Now);
        }
        private bool IsFileAlreadyDownloaded(string url)
        {
            var fileName = Path.GetFileName(new Uri(url).AbsolutePath);
            if (_downloadedUrls.Contains(fileName))
            {
                _logger.LogInformation("File {fileName} has already been downloaded. Skipping.", fileName);
                return true;
            }
            return false;
        }
        private string CreateTitleIdentification(string[]? urlParts)
        {
            var returnedValue = string.Empty;
            try
            {
                if (urlParts is not null && urlParts.Length > 6)
                {
                    returnedValue = $"{urlParts[3]} - {urlParts[4]} - {urlParts[5]} / {urlParts[6]}/{urlParts[7]}";
                }
            }
            catch (NullReferenceException ex)
            {
                _logger.LogCritical("Failed to create a title helper from parts of url. {error}", ex.Message);
            }

            return returnedValue;
        }
        private static List<List<string>> GetUrlGroups(List<string> playerUrls)
        {
            return playerUrls.Select((url, index) => new { Url = url, Index = index })
                .GroupBy(x => x.Index / _urlsPerBrowser)
                .Select(g => g.Select(x => x.Url).ToList())
                .ToList();
        }
        private async Task<(int year, int month, int day, int hour, int minute)> ScrapeDateAndTimeAsync(IPage page)
        {
            int year = 0;
            int month = 0;
            int day = 0;
            int hour = 0;
            int minute = 0;

            try
            {
                var htmlContent = await page.GetContentAsync();
                var htmlDocument = new HtmlDocument();
                htmlDocument.LoadHtml(htmlContent);

                var dateTimeNode = htmlDocument.DocumentNode.SelectSingleNode("//div[@class='article__date']//time/@datetime");

                if (dateTimeNode != null)
                {
                    var dateTimeAttribute = dateTimeNode.GetAttributeValue("datetime", null);
                    if (!string.IsNullOrWhiteSpace(dateTimeAttribute))
                    {
                        var dateTime = DateTime.Parse(dateTimeAttribute);

                        year = dateTime.Year;
                        month = dateTime.Month;
                        day = dateTime.Day;
                        hour = dateTime.Hour;
                        minute = dateTime.Minute;
                    }
                }
            }
            catch (NullReferenceException ex)
            {
                _logger.LogCritical("Failed to find the specified node in the HTML document. {error}", ex.Message);
            }
            catch (FormatException ex)
            {
                _logger.LogCritical("Failed to parse the date and time. {error}", ex.Message);
            }
            return (year, month, day, hour, minute);
        }
        private static (int year, int month) ParseYearAndMonthFromUrlParts(string[] urlParts)
        {
            int year = 0;
            int month = 0;

            if (urlParts.Length < 7)
            {
                return (year, month);
            }

            if (!int.TryParse(urlParts[6], out year) || urlParts[6].Length != 4 ||
                !int.TryParse(urlParts[7], out month) || urlParts[7].Length < 1)
            {
                return (year, month);
            }

            return (year, month);
        }
        private static string[] GetDateAndTimeParts(string[] urlParts)
        {
            var part = urlParts.FirstOrDefault(p => p.Contains("-del-") && p.Contains("-ore-"));

            if (part == null)
            {
                return Array.Empty<string>();
            }

            return part.Split("-del-")[1].Split("-ore-");
        }
        private static bool IsDateAndTimePartsValid(string[] dateAndTimeParts)
        {
            return dateAndTimeParts != null && dateAndTimeParts.Length >= 2 && dateAndTimeParts[0].Length >= 8 && dateAndTimeParts[1].Length >= 4;
        }
        private static (int year, int month, int day, int hour, int minute) ParseDateAndTimeParts(string[] dateAndTimeParts)
        {
            int year = int.Parse(dateAndTimeParts[0].Substring(4, 4));
            int month = int.Parse(dateAndTimeParts[0].Substring(2, 2));
            int day = int.Parse(dateAndTimeParts[0].Substring(0, 2));
            int hour = int.Parse(dateAndTimeParts[1].Substring(0, 2));
            int minute = int.Parse(dateAndTimeParts[1].Substring(2, 2));

            return (year, month, day, hour, minute);
        }
        private async Task<(int year, int month, int day, int hour, int minute)> GetDateAndTimeAsync(string[] urlParts, IPage page)
        {
            int year, month, day, hour, minute;

            string[] dateAndTimeParts = GetDateAndTimeParts(urlParts);

            if (IsDateAndTimePartsValid(dateAndTimeParts))
            {
                (year, month, day, hour, minute) = ParseDateAndTimeParts(dateAndTimeParts);
            }
            else
            {
                (year, month, day, hour, minute) = await ScrapeDateAndTimeAsync(page);
            }

            if (dateAndTimeParts is null && year == 0)
            {
                (year, month) = ParseYearAndMonthFromUrlParts(urlParts);
            }

            return (year, month, day, hour, minute);
        }
        private async Task<IBrowser> LaunchBrowserAsync()
        {
            await _browserFetcher.DownloadAsync(BrowserFetcher.DefaultChromiumRevision);
            var launchOptions = new LaunchOptions
            {
                Headless = true,
                Args = new[] { _browserArg1, _browserArg2 }
            };
            return await Puppeteer.LaunchAsync(launchOptions);
        }
        private static async Task ConfigurePageAsync(IPage page)
        {
            await page.SetUserAgentAsync(GetRandomUserAgent());
            await page.SetRequestInterceptionAsync(true);

            page.Request += async (sender, e) =>
            {
                if (e.Request.ResourceType == ResourceType.Image)
                {
                    await e.Request.AbortAsync();
                }
                else
                {
                    await e.Request.ContinueAsync();
                }
            };
        }
        private string PreparePath(ReiNewsModel model)
        {
            var regionFolder = string.IsNullOrEmpty(model.Region) ? "" : model.Region.Replace(" ", "-");
            var channelFolder = string.IsNullOrEmpty(model.Channel) ? "" : model.Channel.Replace(" ", "-");
            var yearMonthFolder = $"{model.Year}-{model.Month}";

            // Check if day and time values are not null and format accordingly
            string dayTime = (model.Day != 0 && model.Hour != 0 && model.Minute != 0)
                ? $"_{model.Day}_{model.Hour:D2}{model.Minute:D2}"
                : (model.Day != 0 && model.Hour != 0)
                    ? $"_{model.Day}_{model.Hour:D2}00"
                    : "";

            var fileName = $"{channelFolder}_{regionFolder}_{model.Year}_{model.Month}{dayTime}.mp3";

            // Replace spaces in the file name with dashes
            fileName = fileName.Replace(" ", "_");

            if (_outputFolderPath == null)
            {
                return string.Empty;
            }
            var path = Path.Combine(_outputFolderPath, regionFolder, channelFolder, yearMonthFolder, fileName);

            // Create directory if not exists
            var directoryPath = Path.GetDirectoryName(path);
            if (!Directory.Exists(directoryPath) && directoryPath is not null)
            {
                Directory.CreateDirectory(directoryPath);
            }

            return path;
        }
        private static string GetRandomUserAgent()
        {
            Random _random = new();
            List<string> _userAgents = new()
            {
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/74.0.3729.169 Safari/537.36",
                "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_14_6) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/13.1.2 Safari/605.1.15",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:70.0) Gecko/20100101 Firefox/70.0",
                "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_1) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/78.0.3904.108 Safari/537.36",
                "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_1) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/13.0.3 Safari/605.1.15",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:71.0) Gecko/20100101 Firefox/71.0",
                "Mozilla/5.0 (Windows NT 10.0; Trident/7.0; rv:11.0) like Gecko",
                "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_1) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/79.0.3945.88 Safari/537.36",
                "Mozilla/5.0 (X11; Linux x86_64; rv:72.0) Gecko/20100101 Firefox/72.0",
                "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_2) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/79.0.3945.88 Safari/537.36"
            };
            var userAgent = _userAgents[_random.Next(_userAgents.Count)];

            return userAgent;
        }
        //private async Task CheckRaiCookies(IPage page)
        //{
        //    try
        //    {
        //        var cookieBanner = await page.WaitForSelectorAsync(".as-oil__close-banner.as-js-close-banner", new WaitForSelectorOptions { Timeout = 5000 });
        //        if (cookieBanner != null)
        //        {
        //            _logger.LogInformation("Found cookies banner, rejecting cookies...");
        //            await cookieBanner.ClickAsync();
        //            await page.WaitForSelectorAsync(".as-oil__close-banner.as-js-close-banner", new WaitForSelectorOptions { Timeout = 5000, Hidden = true });
        //        }
        //    }
        //    catch (WaitTaskTimeoutException)
        //    {
        //        _logger.LogInformation("Found no cookies banner, continuing...");
        //    }
        //}
        #endregion
    }

}