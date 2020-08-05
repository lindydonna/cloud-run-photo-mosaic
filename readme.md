# Cloud Run Photo Mosaic Generator

This demo generates a photo mosaic from an input image. It uses the Google Cloud Vision API to recognize the image subject, then creates a mosaic from 100 other images of the subject retrieved via a web search.

Here's a photo mosaic of the Orlando Eye.

![Orlando Eye Photo Mosaic](./orlando-eye-both.jpg)

## About this demo

This demo was ported from an [Azure Functions demo](https://github.com/lindydonna/photo-mosaic). The original version used a thin JavaScript client to call functions hosted in Azure. This thin-client pattern is necessary because it is not possible to run a server-side framework on a FaaS platform like Azure Functions.

The Cloud Run version has the following benefits:

- Uses a server-side web framework (ASP.NET). With Cloud Run, there's no need to avoid web frameworks.

- The the entire app can be run locally, without having to install any custom tooling. 

- No change to the developer model. Existing applications can be containerized, and run in a serverless manner, without having to re-architect anything. Plus, developers don't have to learn a new functions framework.

## Running on Cloud Run

This demo is written in C# using ASP.NET. 

To host on Cloud Run, do the following:

1. Get an API key for [Bing Image Search](https://azure.microsoft.com/en-us/services/cognitive-services/bing-image-search-api/). This is needed to perform the image search to retrieve the mosaic tile images.

1. Build a container image from `PhotoMosaicService/Dockerfile` and upload to Google Container Registry.

1. [Create a Cloud Run service](https://cloud.google.com/run/docs/quickstarts/prebuilt-deploy) from the container image above. Set the environment variable `SearchAPIKey` to your Bing Image Search API key.
