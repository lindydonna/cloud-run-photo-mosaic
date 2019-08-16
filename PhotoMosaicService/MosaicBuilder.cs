using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using Google.Cloud.Storage.V1;

public static class MosaicBuilder
{
	public static int TileHeight = 20;
	public static int TileWidth = 20;
	public static int DitheringRadius { get; set; }
	public static int ScaleMultiplier { get; set; }

	static byte[] GetImageAsByteArray(Stream imageStream)
	{
		BinaryReader binaryReader = new BinaryReader(imageStream);
		return binaryReader.ReadBytes((int)imageStream.Length);
	}

	public static Stream GenerateMosaicFromTiles(
		Stream sourceImage,
		string tileBucket, string tileDirectory,
		int tilePixels,
		ILogger logger)
	{
		using (var tileProvider = new QuadrantMatchingTileProvider()) {

			// override default tile width and height if specified
			if (tilePixels != 0) {
				MosaicBuilder.TileWidth = MosaicBuilder.TileHeight = tilePixels;
			}

			MosaicBuilder.DitheringRadius = -1;
			MosaicBuilder.ScaleMultiplier = 1;
			List<byte[]> tileImages = GetTileImages(tileBucket, tileDirectory, logger);

			tileProvider.SetSourceStream(sourceImage);
			tileProvider.ProcessInputImageColors(MosaicBuilder.TileWidth, MosaicBuilder.TileHeight);
			tileProvider.ProcessTileColors(tileImages);

			logger.LogInformation("Generating mosaic...");
			var start = DateTime.Now;

			return GenerateMosaic(tileProvider, sourceImage, tileImages);
    	}
	}

	private static List<byte[]> GetTileImages(
		string tileBucket, string tileDirectory, ILogger logger)
	{
		var cacheDir = Path.Combine(Path.GetTempPath(), "MosaicCache", tileDirectory);
		Directory.CreateDirectory(cacheDir);
		var files = new DirectoryInfo(cacheDir).GetFiles();

		if (files.Length >= 50) {
			return files.Select(x => File.ReadAllBytes(x.FullName)).ToList();
		}

		logger.LogInformation("Downloading tiles images from storage");
		var start = DateTime.Now;

		var storage = StorageClient.Create();
		var options = new ListObjectsOptions() { Delimiter = "/" };
		var objects = storage.ListObjects(tileBucket, tileDirectory + "/", options);

		foreach (var blob in objects) {
			var tempPath = Path.Combine(cacheDir, Guid.NewGuid().ToString());

            using (var outputFile = File.OpenWrite(tempPath)) {
				storage.DownloadObject(tileBucket, $"{tileDirectory}/{blob}", outputFile);
			}

			logger.LogInformation($"Downloaded {blob} to {tempPath}.");
		}

		files = new DirectoryInfo(cacheDir).GetFiles();
		var result = files.Select(x => File.ReadAllBytes(x.FullName)).ToList();

		logger.LogInformation($"Total time to fetch tiles {(DateTime.Now - start).TotalMilliseconds}");

		return result;
	}

	public static void SaveImage(string fullPath, SKImage outImage)
	{
		var imageBytes = outImage.Encode(SKEncodedImageFormat.Jpeg, 80);
		using (var outStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write)) {
			imageBytes.SaveTo(outStream);
		}
	}

	private static Stream GenerateMosaic(
		QuadrantMatchingTileProvider tileProvider, Stream inputStream, 
		List<byte[]> tileImages)
  	{
		SKBitmap[,] mosaicTileGrid;

		inputStream.Seek(0, SeekOrigin.Begin);

		using (var skStream = new SKManagedStream(inputStream))
		using (var bitmap = SKBitmap.Decode(skStream)) {
			// use transparency for the source image overlay
			var srcImagePaint = new SKPaint() { Color = SKColors.White.WithAlpha(200) };

			int xTileCount = bitmap.Width / MosaicBuilder.TileWidth;
			int yTileCount = bitmap.Height / MosaicBuilder.TileHeight;

			int tileCount = xTileCount * yTileCount;

			mosaicTileGrid = new SKBitmap[xTileCount, yTileCount];

			int finalTileWidth = MosaicBuilder.TileWidth * MosaicBuilder.ScaleMultiplier;
			int finalTileHeight = MosaicBuilder.TileHeight * MosaicBuilder.ScaleMultiplier;
			int targetWidth = xTileCount * finalTileWidth;
			int targetHeight = yTileCount * finalTileHeight;

			var tileList = new List<(int, int)>();

			// add coordinates for the left corner of each tile
			for (int x = 0; x < xTileCount; x++) {
				for (int y = 0; y < yTileCount; y++) {
					tileList.Add((x, y));
				}
			}

			// create output surface
			var surface = SKSurface.Create(targetWidth, targetHeight, SKImageInfo.PlatformColorType, SKAlphaType.Premul);
			surface.Canvas.DrawColor(SKColors.White); // clear the canvas / fill with white
			surface.Canvas.DrawBitmap(bitmap, 0, 0, srcImagePaint);

			// using the Darken blend mode causes colors from the source image to come through
			var tilePaint = new SKPaint() { BlendMode = SKBlendMode.Darken };
			surface.Canvas.SaveLayer(tilePaint); // save layer so blend mode is applied

			var random = new Random();

			while (tileList.Count > 0) {
				// choose a new tile at random
				int nextIndex = random.Next(tileList.Count);
				var tileInfo = tileList[nextIndex];
				tileList.RemoveAt(nextIndex);

				// get the tile image for this point
				var tileBitmap = tileProvider.GetImageForTile(tileInfo.Item1, tileInfo.Item2);
				mosaicTileGrid[tileInfo.Item1, tileInfo.Item2] = tileBitmap;

				// draw the tile on the surface at the coordinates
				SKRect tileRect = SKRect.Create(tileInfo.Item1 * TileWidth, tileInfo.Item2 * TileHeight, finalTileWidth, finalTileHeight);
				surface.Canvas.DrawBitmap(tileBitmap, tileRect);
			}

			surface.Canvas.Restore(); // merge layers
			surface.Canvas.Flush();

			var imageBytes = surface.Snapshot().Encode(SKEncodedImageFormat.Jpeg, 80);
			return new MemoryStream(imageBytes.ToArray());
		}
	}

	private static List<string> GetExclusionList(string[,] mosaicTileGrid, int xIndex, int yIndex)
	{
		int xRadius = (MosaicBuilder.DitheringRadius != -1 ? MosaicBuilder.DitheringRadius : mosaicTileGrid.GetLength(0));
		int yRadius = (MosaicBuilder.DitheringRadius != -1 ? MosaicBuilder.DitheringRadius : mosaicTileGrid.GetLength(1));

		var exclusionList = new List<string>();

		return exclusionList;
	}

	public class QuadrantMatchingTileProvider : IDisposable
	{
		internal static int quadrantDivisionCount = 1;
		private Stream inputStream;
		private SKColor[,][,] inputImageRGBGrid;
		private readonly List<(SKBitmap, SKColor[,])> tileImageRGBGridList = new List<(SKBitmap, SKColor[,])>();
		private Random random = new Random();

		public void SetSourceStream(Stream inputStream)
		{
			this.inputStream = inputStream;
			inputStream.Seek(0, SeekOrigin.Begin);
		}

		// Preprocess the quadrants of the input image
		public void ProcessInputImageColors(int tileWidth, int tileHeight)
		{
			using (var skStream = new SKManagedStream(inputStream))
			using (var bitmap = SKBitmap.Decode(skStream)) {
				int xTileCount = bitmap.Width / tileWidth;
				int yTileCount = bitmap.Height / tileHeight;

				int tileDivisionWidth = tileWidth / quadrantDivisionCount;
				int tileDivisionHeight = tileHeight / quadrantDivisionCount;

				int quadrantsCompleted = 0;
				int quadrantsTotal = xTileCount * yTileCount * quadrantDivisionCount * quadrantDivisionCount;
				inputImageRGBGrid = new SKColor[xTileCount, yTileCount][,];

				//Divide the input image into separate tile sections and calculate the average pixel value for each one
				for (int yTileIndex = 0; yTileIndex < yTileCount; yTileIndex++) {
					for (int xTileIndex = 0; xTileIndex < xTileCount; xTileIndex++) {
					var rect = SKRectI.Create(xTileIndex * tileWidth, yTileIndex * tileHeight, tileWidth, tileHeight);
					inputImageRGBGrid[xTileIndex, yTileIndex] = GetAverageColorGrid(bitmap, rect);
					quadrantsCompleted += (quadrantDivisionCount * quadrantDivisionCount);
					}
				}
			}
		}

		// Convert tile images to average color
		public void ProcessTileColors(List<byte[]> tileImages)
		{
			foreach (var bytes in tileImages) {
				var bitmap = SKBitmap.Decode(bytes);

				var rect = SKRectI.Create(0, 0, bitmap.Width, bitmap.Height);
				tileImageRGBGridList.Add((bitmap, GetAverageColorGrid(bitmap, rect)));
			}
		}

		// Returns the best match image per tile area
		public SKBitmap GetImageForTile(int xIndex, int yIndex)
		{
			var tileDistances = new List<(double, SKBitmap)>();

			foreach (var tileGrid in tileImageRGBGridList)
			{
				double distance = 0;

				for (int x = 0; x < quadrantDivisionCount; x++)
					for (int y = 0; y < quadrantDivisionCount; y++) {
						distance +=
							Math.Sqrt(
							Math.Abs(Math.Pow(tileGrid.Item2[x, y].Red, 2) - Math.Pow(inputImageRGBGrid[xIndex, yIndex][x, y].Red, 2)) +
							Math.Abs(Math.Pow(tileGrid.Item2[x, y].Green, 2) - Math.Pow(inputImageRGBGrid[xIndex, yIndex][x, y].Green, 2)) +
							Math.Abs(Math.Pow(tileGrid.Item2[x, y].Blue, 2) - Math.Pow(inputImageRGBGrid[xIndex, yIndex][x, y].Blue, 2)));
					}

				tileDistances.Add((distance, tileGrid.Item1));
			}

			var sorted = tileDistances.OrderBy(item => item.Item1); // sort by best match
			var rand = random.Next(5);
			return rand < 4 ? sorted.First().Item2 : sorted.ElementAt(1).Item2;
		}

		// Converts a portion of the base image to an average RGB color
		private SKColor[,] GetAverageColorGrid(SKBitmap bitmap, SKRectI bounds)
		{
			var rgbGrid = new SKColor[quadrantDivisionCount, quadrantDivisionCount];
			int xDivisionSize = bounds.Width / quadrantDivisionCount;
			int yDivisionSize = bounds.Height / quadrantDivisionCount;

			for (int yDivisionIndex = 0; yDivisionIndex < quadrantDivisionCount; yDivisionIndex++) {
				for (int xDivisionIndex = 0; xDivisionIndex < quadrantDivisionCount; xDivisionIndex++) {

					int pixelCount = 0;
					int totalR = 0, totalG = 0, totalB = 0;

					for (int y = yDivisionIndex * yDivisionSize; y < (yDivisionIndex + 1) * yDivisionSize; y++)
					{
						for (int x = xDivisionIndex * xDivisionSize; x < (xDivisionIndex + 1) * xDivisionSize; x++) {
							var pixel = bitmap.GetPixel(x + bounds.Left, y + bounds.Top);

							totalR += pixel.Red;
							totalG += pixel.Green;
							totalB += pixel.Blue;
							pixelCount++;
						}
					}

					var finalR = (byte)(totalR / pixelCount);
					var finalG = (byte)(totalG / pixelCount);
					var finalB = (byte)(totalB / pixelCount);

					rgbGrid[xDivisionIndex, yDivisionIndex] = new SKColor(finalR, finalG, finalB);
				}
			}

		return rgbGrid;
	}

		public void Dispose()
		{
			foreach (var tileImage in tileImageRGBGridList) {
				tileImage.Item1.Dispose();
			}
		}
	}
}