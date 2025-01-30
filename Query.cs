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
            OrderBy = { "PageTitle", "PageVersion asc" }, // Corrected OrderBy syntax
            Size = 4, // Ensure Size is set to retrieve 4 documents
            Select = { "PageTitle", "PageVersion", "ChunkText", }
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
    private static async Task<string> CompareDocumentsAsync(int initialDocNo, string doc1Text, int compareDocNo, string doc2Text)
    {
        Console.WriteLine($"Comparing document versions:\nDocument Version {initialDocNo}:\n{doc1Text}\nDocument Version {compareDocNo + 1}:\n{doc2Text}\n");
        var systemPrompt = "You are a helpful AI Assistant that is expert at comparing document versions.";
        var userPrompt = $"Provide the differences between the following two documents versions:\n\nDocument Version {initialDocNo}:\n{doc1Text}\n\nDocument Version {compareDocNo + 1}:\n{doc2Text}";

        var payload = new
        {
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            max_tokens = 4096,
            temperature = 0.1
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
    private static void SaveDifferencesToMarkdown(List<(string docNo, string Difference)> differences, string filename = "differences.md")
    {
        using var writer = new StreamWriter(filename, false);
        writer.WriteLine("# Document Differences\n");
        writer.WriteLine($"Date: {DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}\n");
        writer.WriteLine("https://manuals.health.mil/pages/DisplayManualHtmlFile/2025-01-17/AsOf/TOT5/C5S1.html\n");

        foreach (var diff in differences)
        {
            writer.WriteLine($"## Difference between Document Version 1 and Document Version {int.Parse(diff.docNo) + 1}\n");
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
        Console.WriteLine($"Retrieved {documents.Count} documents.");
        if (documents.Count < 4)
        {
            Console.WriteLine("Not enough documents retrieved.");
            return;
        }

        // Compare the documents
        var differences = new List<(string, string)>();
        var docNumber = 0;
        var doc1Text = "";
        var docNText = "";
        foreach (var doc in documents)
        {
            foreach (var kvp in doc)
            {
                //Console.WriteLine($"- {kvp.Key}: {kvp.Value}");
                if (kvp.Key == "ChunkText")
                {
                    Console.WriteLine($"docNumber:{docNumber}-key:{kvp.Key}");
                    if (docNumber == 0)
                    {
                        doc1Text = kvp.Value.ToString();
                    }
                    else
                    {
                        docNText = kvp.Value.ToString();
                    }
                    break;
                }
            }
            if (docNumber == 0) {
                docNumber++;
                continue;   
            }
            var diff = await CompareDocumentsAsync(1, doc1Text, docNumber, docNText);
            differences.Add(($"{docNumber}", diff));
            docNumber++;
        }

        // Save differences to Markdown
        SaveDifferencesToMarkdown(differences);
    }
}