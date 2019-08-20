using Microsoft.AspNetCore.Mvc;
using Google.Cloud.Vision.V1;
using Google.Cloud.Storage.V1;
using System.IO;
using System.Threading.Tasks;
using System;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Linq;

public class MosaicRequest
{
    public string InputImageUrl { get; set; }
    public string OutputFilename { get; set; }
    public string ImageContentString { get; set; }  // if null or empty, use image recognition on the input image
    public int TilePixels { get; set; } // override default value in app settings
}

[ApiController]
[Route("api/[controller]")]
public class MosaicController : Controller
{
    private readonly ILogger logger;
    private const string TileBucket = "lindydonna-mosaic-tiles";
    private const string OutputBucket = "lindydonna-mosaic-output";
    private const string InputBucket = "lindydonna-photo-mosaic";

    public MosaicController(ILogger<MosaicController> logger)
    {
        this.logger = logger;
    }

    [HttpGet]
    public IActionResult Index()
    {
        ViewData["Message"] = "Create a mosaic";
        return View();
    }

    [HttpPost]
    [Route("[action]")]
    public async Task<IActionResult> CreateMosaic(MosaicRequest request)
    {
        var sourceImage = await DownloadFileAsync(request.InputImageUrl);

        var client = ImageAnnotatorClient.Create();

        var image = Image.FromStream(sourceImage);
        var response = client.DetectLabels(image);

        if (response.Count > 0) {
            request.ImageContentString = response.FirstOrDefault().Description;
            logger.LogInformation($"Image label: {request.ImageContentString}");
        }

        await DownloadBingImages(request);
        var tileDirectory = GetStableHash(request.ImageContentString).ToString();

        var stream = MosaicBuilder.GenerateMosaicFromTiles(
            sourceImage, 
            TileBucket, tileDirectory, 
            request.TilePixels, logger);

        var outputFile = $"{Guid.NewGuid().ToString()}.jpg";
        WriteToStorage(OutputBucket, outputFile, stream);

        return Ok($"https://storage.cloud.google.com/{OutputBucket}/{outputFile}");
    }

    [HttpPost]
    [Route("[action]")]
    public async Task<IActionResult> DownloadBingImages(MosaicRequest request)
    {
        string imageKeyword = request.ImageContentString;
        string outputBucket = TileBucket;

        var imageUrls = await DownloadImages.GetImageResultsAsync(imageKeyword, logger);
        var filePrefix = GetStableHash(imageKeyword).ToString();

        if (request.TilePixels == 0) {
            request.TilePixels = MosaicBuilder.TileHeight;
        }
        await DownloadImages.DownloadBingImagesAsync(imageUrls, outputBucket, filePrefix, request.TilePixels, request.TilePixels, logger);

        return Ok();
    }

    private static async Task<Stream> DownloadFileAsync(string inputImageUrl)
    {
        var client = new HttpClient();

        try {
            var bytes = await client.GetByteArrayAsync(inputImageUrl);
            return new MemoryStream(bytes);
            
        }
        catch (Exception) {
            return null;
        }
    }

    private static void WriteToStorage(string bucket, string filename, Stream stream)
    {
        var storage = StorageClient.Create();
        storage.UploadObject(bucket, filename, null, stream);
    }
    
    internal static int GetStableHash(string value)
    {
        if (value == null) {
            throw new ArgumentNullException(nameof(value));
        }

        unchecked {
            int hash = 23;
            foreach (char c in value) {
                hash = (hash * 31) + c;
            }
            return hash;
        }
    }

}