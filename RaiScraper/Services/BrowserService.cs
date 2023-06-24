using PuppeteerSharp;
using RaiScraper.Utilities;

namespace RaiScraper.Services
{
    public class BrowserService : IBrowserService
    {
        private readonly ILogger<BrowserService> _logger;
        private readonly IBrowserGenerator _browserGenerator;
        private readonly BrowserFetcher _browserFetcher;

        public BrowserService(ILogger<BrowserService> logger, IBrowserGenerator browserGenerator)
        {

            _browserFetcher = new BrowserFetcher();


            // Download browser during initialization
            _browserFetcher.DownloadAsync(BrowserFetcher.DefaultChromiumRevision).Wait();
            _logger = logger;
            _browserGenerator = browserGenerator;
        }
        public async Task<IBrowser> LaunchBrowserAsync()
        {
            var browser = await _browserGenerator.GetNewBrowserAsync();
            return browser;
        }

        public async Task CheckRaiCookies(IPage page)
        {
            try
            {
                var cookieBanner = await page.WaitForSelectorAsync(".as-oil__close-banner.as-js-close-banner", new WaitForSelectorOptions { Timeout = 5000 });
                if (cookieBanner != null)
                {
                    _logger.LogInformation("Found cookies banner, rejecting cookies...");
                    await cookieBanner.ClickAsync();
                    await page.WaitForSelectorAsync(".as-oil__close-banner.as-js-close-banner", new WaitForSelectorOptions { Timeout = 5000, Hidden = true });
                }
            }
            catch (WaitTaskTimeoutException)
            {
                _logger.LogInformation("Found no cookies banner, continuing...");
            }
        }

        public void DisposeBrowserFetcher()
        {
            _browserFetcher?.Dispose();
        }

        public async Task<IPage> GetNewPage(IPage page)
        {
            return await _browserGenerator.GetNewPageSettingsAsync(page);
        }
    }
}
