using Microsoft.Extensions.Options;
using PuppeteerSharp;
using RaiScraper.Helpers;
using RaiScraper.Models;
using System.Globalization;

namespace RaiScraper.Services
{
    public class UrlService : IUrlService
    {
        private const int _urlsPerBrowser = 20;
        private readonly ILogger<UrlService> _logger;
        private readonly AppSettingOptions _appSettings;
        private readonly IScraperService _scraperService;
        private readonly string? _outputFolderPath;
        private readonly HashSet<string> _downloadedUrls;

        public UrlService(ILogger<UrlService> logger, IOptions<AppSettingOptions> appSettings, IScraperService scraperService)
        {
            _logger = logger;
            _appSettings = appSettings.Value ?? throw new ArgumentNullException(nameof(appSettings));
            _scraperService = scraperService;
            _outputFolderPath = _appSettings.OutputFolderPath;
            _downloadedUrls = new HashSet<string>();
        }
        public string CreateTitleIdentification(string[]? urlParts, string domain)
        {
            var returnedValue = string.Empty;
            bool isItRaiPlaySound = domain.Contains("raiplaysound");
            try
            {
                if (urlParts is not null && urlParts.Length > 6)
                {
                    if (isItRaiPlaySound)
                    {
                        string url = urlParts[6];
                        returnedValue = url.Replace(".html", "");
                    }
                    else
                    {
                        returnedValue = $"{urlParts[3]} - {urlParts[4]} - {urlParts[5]} / {urlParts[6]}/{urlParts[7]}";
                    }
                }
            }
            catch (NullReferenceException ex)
            {
                _logger.LogCritical("Failed to create a title helper from parts of url. {error}", ex.Message);
            }

            return returnedValue;
        }
        public async Task<(int year, int month, int day, int hour, int minute)> GetDateAndTimeAsync(string[] urlParts, IPage page, string domain)
        {
            int year, month, day, hour, minute;
            bool isItRaiPlaySound = domain.Contains("raiplaysound");

            string[] dateAndTimeParts = isItRaiPlaySound
                ? ParsePartsFromRaiPlayUrl(urlParts)
                : GetDateAndTimeParts(urlParts);

            if (IsDateAndTimePartsValid(dateAndTimeParts, domain))
            {
                (year, month, day, hour, minute) = ParseDateAndTimeParts(dateAndTimeParts, domain);
            }
            else
            {
                (year, month, day, hour, minute) = await _scraperService.ScrapeDateAndTimeAsync(page, domain);
            }

            if (dateAndTimeParts is null && year == 0)
            {
                (year, month) = ParseYearAndMonthFromUrlParts(urlParts, domain);
            }

            return (year, month, day, hour, minute);
        }
        public string PreparePath(RaiNewsModel model)
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

            var fileName = $"{regionFolder}_{channelFolder}_{model.Year}_{model.Month}{dayTime}.mp3";

            // Replace spaces in the file name with dashes
            fileName = fileName.Replace(" ", "_").ToLower(); ;

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
        public List<List<string>> GetUrlGroups(List<string> playerUrls)
        {
            return playerUrls.Select((url, index) => new { Url = url, Index = index })
                .GroupBy(x => x.Index / _urlsPerBrowser)
                .Select(g => g.Select(x => x.Url).ToList())
                .ToList();
        }
        public void InitializeDownloadedUrls()
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
        public bool IsFileAlreadyDownloaded(string url)
        {
            var fileName = Path.GetFileName(new Uri(url).AbsolutePath);
            if (_downloadedUrls.Contains(fileName))
            {
                _logger.LogInformation("File {fileName} has already been downloaded. Skipping.", fileName);
                return true;
            }
            return false;
        }
        private static (int year, int month) ParseYearAndMonthFromUrlParts(string[] urlParts, string domain)
        {
            bool isItRaiPlaySound = domain.Contains("raiplaysound");
            int year = 0;
            int month = 0;

            if (urlParts.Length < 7)
            {
                return (year, month);
            }

            if (isItRaiPlaySound)
            {
                if (int.TryParse(urlParts[4], out year) && int.TryParse(urlParts[5], out month))
                {
                    return (year, month);
                }
            }
            else
            {
                if (!int.TryParse(urlParts[6], out year) || urlParts[6].Length != 4 ||
                !int.TryParse(urlParts[7], out month) || urlParts[7].Length < 1)
                {
                    return (year, month);
                }
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
        private string[] ParsePartsFromRaiPlayUrl(string[] urlParts)
        {
            // Assuming urlParts[4] is "GR-Basilicata-del-30042023-ore-1210-something"
            string dateAndTimePart = urlParts[6];
            string[] parts = dateAndTimePart.Split('-');

            string datePart = parts[3]; // "30042023"
            string timePart = parts[5]; // "1210"

            string year = datePart.Substring(4, 4);
            string month = datePart.Substring(2, 2);
            string day = datePart.Substring(0, 2);

            string hour = timePart.Substring(0, 2);
            string minute = timePart.Substring(2, 2);

            return new[] { year, month, day, hour, minute };
        }
        private static bool IsDateAndTimePartsValid(string[] dateAndTimeParts, string domain)
        {
            bool isItRaiPlaySound = domain.Contains("raiplaysound");
            if (dateAndTimeParts is null)
            {
                return false;
            }
            bool isValid;
            if (isItRaiPlaySound)
            {
                string dateString = string.Join(" ", dateAndTimeParts);
                isValid = DateTime.TryParseExact(
                    dateString,
                    "yyyy MM dd HH mm",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out _);
            }
            else
            {
                isValid = dateAndTimeParts.Length >= 2 && dateAndTimeParts[0].Length >= 8 && dateAndTimeParts[1].Length >= 4;
            }
            return isValid;
        }
        private static (int year, int month, int day, int hour, int minute) ParseDateAndTimeParts(string[] dateAndTimeParts, string domain)
        {
            bool isItRaiPlaySound = domain.Contains("raiplaysound");
            int year, month, day, hour, minute;

            if (isItRaiPlaySound)
            {
                year = int.Parse(dateAndTimeParts[0].ToString().Trim());
                month = int.Parse(dateAndTimeParts[1].ToString().Trim());
                day = int.Parse(dateAndTimeParts[2].ToString().Trim());
                hour = int.Parse(dateAndTimeParts[3].ToString().Trim());
                minute = int.Parse(dateAndTimeParts[4].ToString().Trim());
            }
            else
            {
                year = int.Parse(dateAndTimeParts[0].Substring(4, 4));
                month = int.Parse(dateAndTimeParts[0].Substring(2, 2));
                day = int.Parse(dateAndTimeParts[0].Substring(0, 2));
                hour = int.Parse(dateAndTimeParts[1].Substring(0, 2));
                minute = int.Parse(dateAndTimeParts[1].Substring(2, 2));
            }


            return (year, month, day, hour, minute);
        }
    }
}
