using PuppeteerSharp;

namespace RaiMp3Scraper.Utilities
{
    public interface IBrowserGenerator
    {
        Task<IBrowser> GetNewBrowserAsync();
    }
}