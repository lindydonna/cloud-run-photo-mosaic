using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using System.Web;
using System.Diagnostics;
using System.Threading;
using Google.Cloud.Storage.V1;
using Google.Apis.Storage.v1.Data;
using Microsoft.Extensions.Logging;

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

    var objects = storage.ListObjects(outputBucket, directoryHash + "/", options);
    var objectCount = objects.Count();

    if (objectCount >= MaxImages) {
      logger.LogInformation($"Skipping tile download, have {objectCount} images cached");
      return;
    }

    foreach (var url in imageUrls) {
      try {
        var resizedUrl = $"{url}&w={tileWidth}&h={tileHeight}&c=7";
        var queryString = HttpUtility.ParseQueryString(new Uri(url).Query);
        var imageId = queryString["id"] + ".jpg";

        using (var responseStream = await httpClient.GetStreamAsync(resizedUrl)) {
          logger.LogInformation($"Downloading blob: {imageId}");
          storage.UploadObject(outputBucket, $"{directoryHash}/{imageId}", null, responseStream);
        }
      }
      catch (Exception e) {
        logger.LogInformation($"Exception downloading blob: {e.Message}");
        continue;
      }
    }
  }
}
