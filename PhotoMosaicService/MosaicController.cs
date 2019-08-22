using Microsoft.AspNetCore.Mvc;
using Google.Cloud.Vision.V1;
using Google.Cloud.Storage.V1;
using System.IO;
using System.Threading.Tasks;
using System;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Linq;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using System;

public class MosaicRequest
{
    public string InputImageUrl { get; set; }
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
        var inputImage = await DownloadFileAsync(request.InputImageUrl);
        var mosaicFile = await CreateMosaicFromStreamAsync(inputImage, request.TilePixels);

        return Ok(mosaicFile);
    }

    private async Task<string> CreateMosaicFromStreamAsync(
        Stream inputImage, int tilePixels = 20)
    {
        var client = ImageAnnotatorClient.Create();

        var image = Image.FromStream(inputImage);
        var response = client.DetectLabels(image);
        string imageLabel = "cat"; // default image label is "cat"

        if (response.Count > 0) {
            imageLabel = response.FirstOrDefault().Description;
            logger.LogInformation($"Image label: {imageLabel}");
        }

        await DownloadBingImages(imageLabel, tilePixels);
        var tileDirectory = GetStableHash(imageLabel).ToString();

        var stream = MosaicBuilder.GenerateMosaicFromTiles(
            inputImage, 
            TileBucket, tileDirectory, 
            tilePixels, logger);

        var outputFile = $"{Guid.NewGuid().ToString()}.jpg";
        WriteToStorage(OutputBucket, outputFile, stream);

        return outputFile;
    }

    [HttpPost]
    [Route("[action]")]
    public async Task<IActionResult> DownloadBingImages(string imageLabel, int tilePixels)
    {
        string outputBucket = TileBucket;

        var filePrefix = GetStableHash(imageLabel).ToString();

        await DownloadImages.DownloadBingImagesAsync(
            imageLabel, outputBucket, 
            filePrefix, tilePixels, logger);

        return Ok();
    }

    [HttpPost]
    public async Task<IActionResult> UploadFile([FromForm] IFormFile file)
    {
        // TODO: check file extension  
        if (file.Length > 0) {
            var outputFilename = await CreateMosaicFromStreamAsync(file.OpenReadStream());
    
            // download image from storage
            var storage = StorageClient.Create();
            var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid().ToString()}.jpg");

            using (var tempFile = System.IO.File.OpenWrite(tempPath)) {
                storage.DownloadObject(OutputBucket, outputFilename, tempFile);
                logger.LogInformation($"Downloaded {outputFilename} to {tempPath}.");

                ViewData["imagePath"] = tempPath;
            }

            return View("Index");
        }

        return BadRequest("No input image");
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