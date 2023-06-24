using PuppeteerSharp;

namespace RaiScraper.Services
{
    public interface IScraperService
    {
        Task<(int year, int month, int day, int hour, int minute)> ScrapeDateAndTimeAsync(IPage page, string domain);
        Task<List<string>> ScrapeSourceUrlsToHtmlAsync(string sitemapUrl);
    }
}