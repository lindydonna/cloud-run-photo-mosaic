using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using System.Web;
using Google.Cloud.Storage.V1;
using Microsoft.Extensions.Logging;
using System.IO;
using System.IO.Compression;

public static class DownloadImages
{
    private const string SubscriptionKeyHeader = "Ocp-Apim-Subscription-Key";
    private const string SearchApiKeyName = "SearchAPIKey";
    private const string BingSearchUri = "https://api.cognitive.microsoft.com/bing/v7.0/images/search";

    private const int MaxImages = 100;

    public static async Task<List<string>> GetImageResultsAsync(string searchTerm, ILogger logger)
    {
        var result = new List<string>();

        try {
            var httpClient = new HttpClient();

            var builder = new UriBuilder(BingSearchUri);
            var queryParams = HttpUtility.ParseQueryString(string.Empty);
            queryParams["q"] = searchTerm.ToString();
            queryParams["count"] = MaxImages.ToString();

            builder.Query = queryParams.ToString();

            var request = new HttpRequestMessage() {
                RequestUri = builder.Uri,
                Method = HttpMethod.Get
            };

            var apiKey = Environment.GetEnvironmentVariable(SearchApiKeyName);
            request.Headers.Add(SubscriptionKeyHeader, apiKey);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (compatible; MSIE 10.0; Windows Phone 8.0; Trident/6.0; IEMobile/10.0; ARM; Touch; NOKIA; Lumia 822)");

            var response = await httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode) {
                var resultString = await response.Content.ReadAsStringAsync();
                var resultObject = JObject.Parse(resultString);

                if (resultObject == null) {
                    logger.LogError("ERROR: No results from image search");
                }

                var images = resultObject["value"];

                foreach (var imageInfo in images) {
                    result.Add(imageInfo["thumbnailUrl"].ToString());
                }
            }
            else {
                logger.LogError($"Image search returned error: {response.ToString()}");
            }
        }
        catch (Exception e) {
            logger.LogError($"Exception during image search: {e.Message}");
        }

        return result;
    }

    public static async Task DownloadBingImagesAsync(
        List<string> imageUrls,
        string outputBucket, string directoryHash,
        int tileWidth, int tileHeight,
        ILogger logger)
    {
        var httpClient = new HttpClient();
        var storage = StorageClient.Create();

        var options = new ListObjectsOptions() { Delimiter = "/" };

        var zipFilename = $"{directoryHash}.zip";
        var cacheDir = Path.Combine(Path.GetTempPath(), "MosaicCache", directoryHash);
        var zipPath = Path.Combine(Path.GetTempPath(), "MosaicCache", zipFilename);
        Directory.CreateDirectory(cacheDir);

        var objects = storage.ListObjects(outputBucket, zipFilename, null);

        if (File.Exists(zipPath) || objects.Count() > 0) {
            logger.LogInformation($"Zipfile already exists, skipping Bing image download");
            return;
        }

        foreach (var url in imageUrls) {
            try {
                var resizedUrl = $"{url}&w={tileWidth}&h={tileHeight}&c=7";
                var queryString = HttpUtility.ParseQueryString(new Uri(url).Query);
                var imageId = queryString["id"] + ".jpg";
                var filePath = Path.Combine(cacheDir, imageId);

                using (var responseStream = await httpClient.GetStreamAsync(resizedUrl)) {
                    logger.LogInformation($"Downloading blob: {filePath}");

                    using (var outputFileStream = File.Create(filePath)) {
                        responseStream.CopyTo(outputFileStream);
                    }
                }
            }
            catch (Exception e) {
                logger.LogInformation($"Exception downloading blob: {e.Message}");
                continue;
            }
        }

        ZipFile.CreateFromDirectory(cacheDir, zipPath);

        using (var zipStream = File.Open(zipPath, FileMode.Open)) {
            if (zipStream != null) {
                storage.UploadObject(outputBucket, zipFilename, null, zipStream);
            }
            else {
                logger.LogError($"Zip file {zipPath} does not exist!");
            }
        }
    }
}
