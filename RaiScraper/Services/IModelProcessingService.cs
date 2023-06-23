using PuppeteerSharp;
using RaiScraper.Models;

namespace RaiScraper.Services
{
    public interface IModelProcessingService
    {
        Task<RaiNewsModel> CreateModelFromUrlAsync(string url, IBrowser browser);
        bool IsValidReiNewsModel(RaiNewsModel rnModel);
        Task ProcessIndividualReiNewsModel(RaiNewsModel rnModel, CancellationToken stoppingToken);
    }
}