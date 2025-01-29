using Azure.Storage.Blobs;
using Azure.Identity;
using DotNetEnv;
using DocumentFormat.OpenXml.Packaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

class Upload
{
    // Main method
    static async Task Main(string[] args)
    {
        // Load environment variables
        Env.Load();
        var StorageAccount = Environment.GetEnvironmentVariable("AZURE_STORAGE_ACCOUNT");
        var StorageAccountContainer = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONTAINER");

        // Validate environment variables
        if (string.IsNullOrEmpty(StorageAccount) || string.IsNullOrEmpty(StorageAccountContainer))
        {
            Console.WriteLine("One or more Azure Storage environment variables are missing.");
            return;
        }

        // Load environment variables
        var docsDirectory = Path.Combine(Directory.GetCurrentDirectory(), "docs");
        if (!Directory.Exists(docsDirectory))
        {
            Console.WriteLine($"Docs directory does not exist: {docsDirectory}");
            return;
        }

        // Extract text from Word documents
        var extractedTexts = new List<string>();
        foreach (var filePath in Directory.GetFiles(docsDirectory, "*.docx"))
        {
            using (WordprocessingDocument wordDoc = WordprocessingDocument.Open(filePath, false))
            {
                var body = wordDoc.MainDocumentPart.Document.Body;
                extractedTexts.Add(body.InnerText);
            }
        }

        // Upload extracted content to Azure Blob
        var blobServiceClient = new BlobServiceClient(
            new Uri($"https://{StorageAccount}.blob.core.windows.net"),
            new DefaultAzureCredential());
        var containerName = StorageAccountContainer;
        var containerClient = blobServiceClient.GetBlobContainerClient(containerName);

        foreach (var extractedText in extractedTexts)
        {
            var blobName = Path.GetFileNameWithoutExtension(Guid.NewGuid().ToString()) + ".txt";
            var blobClient = containerClient.GetBlobClient(blobName);
            using (var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(extractedText)))
            {
                await blobClient.UploadAsync(stream, overwrite: true);
            }
            Console.WriteLine($"Content uploaded to blob '{blobName}'.");
        }
    }
}
