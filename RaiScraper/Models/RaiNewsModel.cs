namespace RaiScraper.Models
{
    public class RaiNewsModel
    {
        public int Id { get; set; }
        public string? Title { get; set; }
        public string? SourceUrl { get; set; }
        public string? Mp3Url { get; set; }
        public string? Mp4Url { get; set; }
        public string? Region { get; set; }
        public string? Channel { get; set; }
        public int Year { get; set; }
        public int Month { get; set; }
        public int Day { get; set; }
        public int Hour { get; set; }
        public int Minute { get; set; }
    }
}
