using RaiScraper.Models;

namespace RaiScraper.Services
{
    public interface IDownloadService
    {
        Task DownloadMedium(RaiNewsModel model);
    }
}