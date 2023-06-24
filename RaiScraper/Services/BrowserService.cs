using Microsoft.Extensions.Options;
using PuppeteerExtraSharp.Plugins.ExtraStealth;
using PuppeteerExtraSharp;
using PuppeteerSharp;
using RaiScraper.Helpers;
using PuppeteerExtraSharp.Plugins.ExtraStealth.Evasions;
using PuppeteerExtraSharp.Plugins.AnonymizeUa;

namespace RaiScraper.Services
{
    public class BrowserService : IBrowserService
    {
        private readonly ILogger<BrowserService> _logger;
        private readonly AppSettingOptions _appSettings;
        private readonly BrowserFetcher _browserFetcher;
        private readonly string _browserArg1;
        private readonly string _browserArg2;
        public BrowserService(ILogger<BrowserService> logger, IOptions<AppSettingOptions> appSettings)
        {
            if (appSettings is null)
            {
                throw new ArgumentNullException(nameof(appSettings));
            }

            _browserFetcher = new BrowserFetcher();
            _browserArg1 = "--disable-web-security";
            _browserArg2 = "--disable-setuid-sandbox";

            // Download browser during initialization
            //_browserFetcher.DownloadAsync(BrowserFetcher.DefaultChromiumRevision).Wait();
            _logger = logger;
            _appSettings = appSettings.Value;
        }
        public async Task<IBrowser> LaunchBrowserAsync()
        {
            var launchOptions = new LaunchOptions
            {
                Headless = true,
                ExecutablePath = _appSettings.ChromePath,
                Args = new[] { _browserArg1, _browserArg2 }
            };
            return await Puppeteer.LaunchAsync(launchOptions);
        }
        public string GetRandomUserAgent()
        {
            Random _random = new();
            List<string> _userAgents = new()
            {
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/58.0.3029.110 Safari/537.3",
                //"Mozilla/5.0 (Macintosh; Intel Mac OS X 10_14_6) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/13.1.2 Safari/605.1.15",
                //"Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:70.0) Gecko/20100101 Firefox/70.0",
                //"Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_1) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/78.0.3904.108 Safari/537.36",
                //"Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_1) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/13.0.3 Safari/605.1.15",
                //"Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:71.0) Gecko/20100101 Firefox/71.0",
                //"Mozilla/5.0 (Windows NT 10.0; Trident/7.0; rv:11.0) like Gecko",
                //"Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_1) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/79.0.3945.88 Safari/537.36",
                //"Mozilla/5.0 (X11; Linux x86_64; rv:72.0) Gecko/20100101 Firefox/72.0",
                //"Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_2) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/79.0.3945.88 Safari/537.36"
            };
            var userAgent = _userAgents[_random.Next(_userAgents.Count)];

            return "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/58.0.3029.110 Safari/537.3";
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
    }
}
