using PuppeteerSharp;
using RaiScraper.Models;

namespace RaiScraper.Services
{
    public interface IUrlService
    {
        string CreateTitleIdentification(string[]? urlParts, string domain);
        Task<(int year, int month, int day, int hour, int minute)> GetDateAndTimeAsync(string[] urlParts, IPage page, string domain);
        List<List<string>> GetUrlGroups(List<string> playerUrls);
        void InitializeDownloadedUrls();
        bool IsFileAlreadyDownloaded(string url);
        string PreparePath(RaiNewsModel model);
    }
}