using System;
using Azure.Core;
using Azure;
using Azure.Identity;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Azure.Search.Documents.Indexes;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using DotNetEnv;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text;

// Define the SearchDocument class
public class SearchDocument : Dictionary<string, object> { }

public class Query
{

    // Add a method to retrieve documents ordered by PageVersion
    private static async Task<List<SearchDocument>> RetrieveDocumentsOrdered(SearchClient searchClient)
    {
        var options = new SearchOptions
        {
            OrderBy = { "PageTitle, PageVersion asc" },
            Size = 4, // Assuming 4 documents
            Select = { "PageTitle", "ChunkText", "PageVersion" }
        };

        var results = await searchClient.SearchAsync<SearchDocument>("*", options);
        var documents = new List<SearchDocument>();

        await foreach (var result in results.Value.GetResultsAsync())
        {
            documents.Add(result.Document);
        }

        return documents;
    }

    // Add a method to compare two documents using a language model
    private static async Task<string> CompareDocumentsAsync(string doc1, string doc2)
    {
        var prompt = $"Provide the differences between the following two documents:\n\nDocument 1:\n{doc1}\n\nDocument 2:\n{doc2}";

        var payload = new
        {
            messages = new[]
            {
                new { role = "system", content = Environment.GetEnvironmentVariable("SYSTEM_MESSAGE") },
                new { role = "user", content = prompt }
            },
            max_tokens = 1000,
            temperature = 0.3
        };

        var jsonPayload = JsonSerializer.Serialize(payload);
        var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        using var httpClient = new HttpClient();
        var credential = new DefaultAzureCredential();
        var token = await credential.GetTokenAsync(new Azure.Core.TokenRequestContext(new[] { "https://cognitiveservices.azure.com/.default" }));
        httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Token);

        var response = await httpClient.PostAsync(
            $"{Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")}/openai/deployments/{Environment.GetEnvironmentVariable("MODEL_CHAT_DEPLOYMENT_NAME")}/chat/completions?api-version=2023-05-15",
            content
        );

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"Error: {response.StatusCode}");
            Console.WriteLine($"Response Body: {errorContent}");
            throw new HttpRequestException($"Request failed with status code {response.StatusCode}: {errorContent}");
        }

        var responseBody = await response.Content.ReadAsStringAsync();
        var jsonDoc = JsonDocument.Parse(responseBody);
        return jsonDoc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
    }

    // Add a method to save differences to a Markdown file
    private static void SaveDifferencesToMarkdown(List<(string Doc2, string Difference)> differences, string filename = "differences.md")
    {
        using var writer = new StreamWriter(filename, false);
        writer.WriteLine("# Document Differences\n");
        writer.WriteLine($"Date: {DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}\n");

        foreach (var diff in differences)
        {
            writer.WriteLine($"## Difference between Document 1 and Document {diff.Doc2}\n");
            writer.WriteLine($"{diff.Difference}\n");
        }

        Console.WriteLine($"Differences saved to {filename}");
    }

    public static async System.Threading.Tasks.Task Main(string[] args)
    {
        // Load environment variables
        Env.Load();

        var requiredVars = new string[] {
            "AZURE_OPENAI_ENDPOINT",
            "AISEARCH_ENDPOINT",
            "AISEARCH_INDEXNAME"
        };
        foreach (var varName in requiredVars)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(varName)))
            {
                Console.WriteLine($"Missing required environment variable: {varName}");
                return;
            }
        }

        // Create SearchClient
        var searchEndpoint = Environment.GetEnvironmentVariable("AISEARCH_ENDPOINT");
        var indexName = Environment.GetEnvironmentVariable("AISEARCH_INDEXNAME");
        var credential = new DefaultAzureCredential();
        var searchClient = new SearchClient(new Uri(searchEndpoint), indexName, credential);

        // Retrieve documents ordered by PageVersion
        var documents = await RetrieveDocumentsOrdered(searchClient);
        if (documents.Count < 4)
        {
            Console.WriteLine("Not enough documents retrieved.");
            return;
        }

        var doc1 = documents[0]["ChunkText"] as string;
        var differences = new List<(string, string)>();

        // Compare Document 1 with Documents 2, 3, and 4
        for (int i = 1; i < 4; i++)
        {
            var docN = documents[i]["ChunkText"] as string;
            var diff = await CompareDocumentsAsync(doc1, docN);
            differences.Add(($"{i + 1}", diff));
        }

        // Save differences to Markdown
        SaveDifferencesToMarkdown(differences);
    }
}