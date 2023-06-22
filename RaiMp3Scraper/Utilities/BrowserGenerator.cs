using PuppeteerSharp;

namespace RaiMp3Scraper.Utilities
{
    public class BrowserGenerator : IBrowserGenerator
    {
        private static readonly List<string> _userAgents = new()
        {
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/74.0.3729.169 Safari/537.36",
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_14_6) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/13.1.2 Safari/605.1.15",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:70.0) Gecko/20100101 Firefox/70.0",
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_1) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/78.0.3904.108 Safari/537.36",
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_1) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/13.0.3 Safari/605.1.15",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:71.0) Gecko/20100101 Firefox/71.0",
            "Mozilla/5.0 (Windows NT 10.0; Trident/7.0; rv:11.0) like Gecko",
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_1) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/79.0.3945.88 Safari/537.36",
            "Mozilla/5.0 (X11; Linux x86_64; rv:72.0) Gecko/20100101 Firefox/72.0",
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_2) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/79.0.3945.88 Safari/537.36"
        };


        private static readonly List<string> _screenResolutions = new()
        {
            "1920x1080",
            "1366x768",
            "1440x900",
            "1600x900",
            "2560x1440"
        };


        private static readonly List<string> _referersEnglish = new()
        {
            "{0} news",
            "news from {0}",
            "Italy news by {0}",
            "daily news from Italy {0}"
        };

        private static readonly List<string> _referersItalian = new()
        {
            "notizie da {0}",
            "notizie da {0}",
            "Notizie dall'Italia da {0}",
            "notizie quotidiane dall'Italia {0}",
            "{0} news",
            "notizie rai",
            "rai tgr",
            "rai gr",
            "rai giornale radio",
            "rai notiziari regionali",
            "tg regionali rai"
        };


        //private static readonly List<string> _connectionTypes = new()
        //{
        //    "DSL",
        //    "Cable",
        //    "Fiber",
        //    "Satellite",
        //    "Dial-up",
        //    "4G",
        //    "5G"
        //};


        private static readonly List<string> _timezones = new()
        {
            "America/New_York",
            "Europe/Rome",
            "Europe/Rome"
        };


        private readonly Random _random = new();

        public async Task<IBrowser> GetNewBrowserAsync()
        {
            var timezone = _timezones[_random.Next(_timezones.Count)];
            bool isItalian = timezone == "Europe/Rome";

            var userAgent = _userAgents[_random.Next(_userAgents.Count)];
            var resolution = _screenResolutions[_random.Next(_screenResolutions.Count)];
            string refererTemplate = "https://www.google.com/search?q={0}";
            var query = isItalian ? _referersItalian[_random.Next(_referersItalian.Count)] : _referersEnglish[_random.Next(_referersEnglish.Count)];
            string referer = string.Format(refererTemplate, query);
            // var _connectionType = ConnectionTypes[_random.Next(ConnectionTypes.Count)];

            var options = new LaunchOptions
            {
                Headless = true,
                DefaultViewport = GetViewport(resolution),
                Args = new[]
                {
                $"--user-agent={userAgent}",
                $"--referer={referer}",
                $"--lang={(isItalian ? "it" : "en")}",
                $"--window-size={resolution}",
                $"--disable-web-security",
                $"--disable-features=DoNotTrack", // Do Not Track as false
                $"--disable-features=IsolateOrigins,site-per-process",
                $"--timezoneId={timezone}"
            }
            };

            var browser = await Puppeteer.LaunchAsync(options);
            return browser;
        }

        private static ViewPortOptions GetViewport(string resolution)
        {
            var parts = resolution.Split('x');
            return new ViewPortOptions { Width = int.Parse(parts[0]), Height = int.Parse(parts[1]) };
        }
    }
}
