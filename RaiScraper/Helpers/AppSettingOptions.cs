namespace RaiScraper.Helpers
{
    public class AppSettingOptions
    {
        public string? OutputFolderPath { get; set; }
        public List<string>? UrlAddressesToParse { get; set; }
        public string? FFmpegPath { get; set; }
        public DateTime DateFrom { get; set; } = DateTime.MinValue;
        public DateTime DateTo { get; set; } = DateTime.MaxValue;
        public string? LogFilePath { get; set; }
        public string? DownloadInfoPath { get; set; }
        public List<int>? ParsingHours { get; set; }  // list of hours to run parsing
        public int MaxConcurrentDownloads { get; set; } = 4; //default 4
        public int RandomValueFrom { get; set; } = 6000;
        public int RandomValueTo { get; set; } = 10000;
    }
}
