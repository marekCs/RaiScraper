using PuppeteerSharp;

namespace RaiScraper.Utilities
{
    public interface IBrowserGenerator
    {
        Task<IBrowser> GetNewBrowserAsync();
    }
}