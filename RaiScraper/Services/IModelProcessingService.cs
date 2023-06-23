using PuppeteerSharp;
using RaiScraper.Models;

namespace RaiScraper.Services
{
    public interface IModelProcessingService
    {
        Task<RaiNewsModel> CreateModelFromUrlAsync(string url, IPage page);
        bool IsValidReiNewsModel(RaiNewsModel rnModel);
        Task ProcessIndividualReiNewsModel(RaiNewsModel rnModel, CancellationToken stoppingToken);
    }
}