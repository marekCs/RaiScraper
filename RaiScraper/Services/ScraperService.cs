using HtmlAgilityPack;
using Microsoft.Extensions.Options;
using PuppeteerSharp;
using RaiScraper.Helpers;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace RaiScraper.Services
{
    public class ScraperService : IScraperService
    {
        private readonly ILogger<ScraperService> _logger;
        private readonly AppSettingOptions _appSettingsOptions;
        private readonly IBrowserService _browserService;
        private readonly DateTime _dateFrom;
        private readonly DateTime _dateTo;

        public ScraperService(ILogger<ScraperService> logger, IBrowserService browserService, IOptions<AppSettingOptions> appSettings)
        {
            _logger = logger;
            _appSettingsOptions = appSettings.Value ?? throw new ArgumentNullException(nameof(appSettings));
            _browserService = browserService;
            _dateFrom = _appSettingsOptions.DateFrom;
            _dateTo = _appSettingsOptions.DateTo;
        }
        public async Task<List<string>> ScrapeSourceUrlsToHtmlAsync(string sitemapUrl)
        {
            if (sitemapUrl.EndsWith(".xml"))
            {
                return await ScrapeUrlsFromXmlAsync(sitemapUrl);
            }
            else
            {
                return await ScrapeUrlsFromHtmlAsync(sitemapUrl);
            }
        }
        public async Task<(int year, int month, int day, int hour, int minute)> ScrapeDateAndTimeAsync(IPage page, string domain)
        {
            bool isItRaiPlaySound = domain.Contains("raiplaysound");
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

                if (isItRaiPlaySound)
                {
                    var dateTimeNode = htmlDocument.DocumentNode.SelectSingleNode("//h1");
                    if (dateTimeNode != null)
                    {
                        string pattern = @"\b\d{2}\/\d{2}\/\d{4} ore \d{2}:\d{2}\b";
                        var regex = new Regex(pattern);
                        var match = regex.Match(dateTimeNode.InnerHtml);

                        if (match.Success)
                        {
                            var dateTimeString = match.Value.Replace(" ore ", " ");
                            var format = "dd/MM/yyyy HH:mm";
                            var dateTime = DateTime.ParseExact(dateTimeString, format, CultureInfo.InvariantCulture);

                            year = dateTime.Year;
                            month = dateTime.Month;
                            day = dateTime.Day;
                            hour = dateTime.Hour;
                            minute = dateTime.Minute;
                        }
                    }
                }
                else
                {
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
        private async Task<List<string>> ScrapeUrlsFromXmlAsync(string xmlUrl)
        {
            bool filterMustBeApplied = false;
            var httpClient = new HttpClient();
            var xmlContent = await httpClient.GetStringAsync(xmlUrl);

            var xmlDoc = XDocument.Parse(xmlContent);
            XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
            var urlNodes = xmlDoc.Descendants(ns + "loc");
            var noOfHtmlUrls = urlNodes.Count();

            _logger.LogInformation("This XML url contains in total {count} html web pages.", noOfHtmlUrls);
            // if there is a filter on the specific dates we want to parse
            if (_dateFrom != DateTime.MinValue && _dateTo != DateTime.MaxValue)
            {
                filterMustBeApplied = true;
                _logger.LogWarning("Looks like we'll have to apply the filter to the desired time period from {_dateFrom} to {_dateTo}.", _dateFrom, _dateTo);
            }

            // Filter URLs that contain "/audio/" or "/video/"
            var audioVideoUrls = urlNodes
                .Select(n => n.Value)
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
        private async Task<List<string>> ScrapeUrlsFromHtmlAsync(string htmlUrl)
        {
            bool filterMustBeApplied = false;

            var browser = await _browserService.LaunchBrowserAsync();
            var page = await browser.NewPageAsync();
            List<string>? dateFilteredUrls = new();
            try
            {
                await page.GoToAsync(htmlUrl);
                await page.WaitForTimeoutAsync(2000);
                var content = await page.GetContentAsync();

                // Parse content using HtmlAgilityPack
                var doc = new HtmlDocument();
                doc.LoadHtml(content);

                var urls = new List<string>();
                var nodes = doc.DocumentNode.SelectNodes("//div[@role='listitem']/article[@class='relative']/a[@class='relative group block']");

                if (nodes != null)
                {
                    foreach (var node in nodes)
                    {
                        var hrefValue = node.GetAttributeValue("href", string.Empty);
                        if (!string.IsNullOrEmpty(hrefValue))
                        {
                            urls.Add("https://www.raiplaysound.it" + hrefValue);
                        }
                    }
                }

                _logger.LogInformation("This HTML url contains in total {count} html web pages.", urls.Count);
                // if there is a filter on the specific dates we want to parse
                if (_dateFrom != DateTime.MinValue && _dateTo != DateTime.MaxValue)
                {
                    filterMustBeApplied = true;
                    _logger.LogWarning("Looks like we'll have to apply the filter to the desired time period from {_dateFrom} to {_dateTo}.", _dateFrom, _dateTo);
                }

                // Filter URLs that contain "/audio/" or "/video/"
                var audioVideoUrls = urls
                    .Where(url => url.Contains("/audio/") || url.Contains("/video/"))
                    .ToList();

                if (!filterMustBeApplied)
                {
                    return audioVideoUrls;
                }

                // If a filter is to be applied, we get the URLs that match the specified date
                dateFilteredUrls = audioVideoUrls
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
            catch (Exception ex)
            {
                _logger.LogError("Error during ParseUrlsFromHtmlAsync() operation. {message}", ex.Message);
                return dateFilteredUrls;
            }
            finally
            {
                await page.CloseAsync();
                await page.DisposeAsync();
                if (browser != null)
                {
                    await browser.CloseAsync();
                    await browser.DisposeAsync();
                    browser = null; // To prevent further use of the browser object
                }
            }

        }
    }
}
