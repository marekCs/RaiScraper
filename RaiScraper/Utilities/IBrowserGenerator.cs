using PuppeteerSharp;

namespace RaiScraper.Utilities
{
    public interface IBrowserGenerator
    {
        Task<IBrowser> GetNewBrowserAsync();
        Task<IPage> GetNewPageSettingsAsync(IPage page);
    }
}