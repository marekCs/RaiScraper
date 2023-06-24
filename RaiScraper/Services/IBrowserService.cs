using PuppeteerSharp;

namespace RaiScraper.Services
{
    public interface IBrowserService
    {
        Task CheckRaiCookies(IPage page);
        void DisposeBrowserFetcher();
        string GetRandomUserAgent();
        Task<IBrowser> LaunchBrowserAsync();
    }
}