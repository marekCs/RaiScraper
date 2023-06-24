using PuppeteerSharp;

namespace RaiScraper.Services
{
    public interface IBrowserService
    {
        Task CheckRaiCookies(IPage page);
        void DisposeBrowserFetcher();
        Task<IPage> GetNewPage(IPage page);
        Task<IBrowser> LaunchBrowserAsync();
    }
}