using Microsoft.AspNetCore.Mvc;
using Google.Cloud.Vision.V1;
using Google.Cloud.Storage.V1;
using System.IO;
using System.Threading.Tasks;
using System.Diagnostics;
using System;
using Microsoft.Extensions.Logging;

public class MosaicRequest
{
	public string InputImageUrl { get; set; }
	public string OutputFilename { get; set; }
	public string ImageContentString { get; set; }  // if null or empty, use image recognition on the input image
	public int TilePixels { get; set; } // override default value in app settings
}

[ApiController]
[Route("api/[controller]")]
public class MosaicController : ControllerBase
{
	private readonly ILogger logger;

	public MosaicController(ILogger<MosaicController> logger)
	{
		this.logger = logger;
	}

	[HttpGet]
	public string GetAll()
	{
		return "Hello World!!!!!";
	}

	// POST: api/detectLabel
	[HttpPost]
	[Route("[action]")]
	public IActionResult DetectLabel(MosaicRequest request)
	{
		// Instantiates a client
		var client = ImageAnnotatorClient.Create();
		// Load the image file into memory
		var image = Image.FromUri(request.InputImageUrl);
		// Performs label detection on the image file
		var response = client.DetectLabels(image);
		// foreach (var annotation in response)
		// {
		//     if (annotation.Description != null)
		//         Console.WriteLine(annotation.Description);
		// }

		return Ok(response);
	}

	// // POST: api/
	// [HttpPost]
	// public IActionResult CreateMosaic(MosaicRequest request)
	// {

	// }

	// POST: api/downloadImages
	[HttpPost]
	[Route("[action]")]
	public async Task<IActionResult> DownloadBingImages(MosaicRequest request)
	{
		string imageKeyword = request.ImageContentString;
		string outputBucket = "lindydonna-photo-mosaic";

		var imageUrls = await DownloadImages.GetImageResultsAsync(imageKeyword, logger);
		var filePrefix = GetStableHash(imageKeyword).ToString();
		logger.LogInformation($"Query hash: {filePrefix}");

		await DownloadImages.DownloadBingImagesAsync(imageUrls, outputBucket, filePrefix, 20, 20, logger);

		return Ok(filePrefix);
	}

	/// <summary>
	/// Computes a stable non-cryptographic hash
	/// </summary>
	/// <param name="value">The string to use for computation</param>
	/// <returns>A stable, non-cryptographic, hash</returns>
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
