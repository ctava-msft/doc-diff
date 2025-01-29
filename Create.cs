using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Azure;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using DotNetEnv;
using Azure.Identity;

public class CreateIndex
{
    private static List<SearchField> LoadFieldsFromJson(string path)
    {
        var json = File.ReadAllText(path);
        var doc = JsonDocument.Parse(json);
        var fieldsArray = doc.RootElement.GetProperty("fields");
        var fields = new List<SearchField>();
        foreach (var f in fieldsArray.EnumerateArray())
        {
            var name = f.GetProperty("name").GetString();
            var dataType = f.GetProperty("type").GetString();
            bool isKey = f.GetProperty("key").GetBoolean();

            // Determine the appropriate field type based on the field properties
            SearchField field;

            bool isSearchable = f.GetProperty("searchable").GetBoolean();
            bool isFilterable = f.GetProperty("filterable").GetBoolean();
            bool isSortable = f.GetProperty("sortable").GetBoolean();
            bool isFacetable = f.GetProperty("facetable").GetBoolean();
            // bool isRetrievable = f.GetProperty("retrievable").GetBoolean(); // Removed to fix CS0117 errors

            if (dataType.StartsWith("Collection"))
            {
                // Handle collection types
                var fieldType = SearchFieldDataType.Collection(SearchFieldDataType.Single);
                if (isSearchable)
                {
                    field = new SearchableField(name, fieldType)
                    {
                        IsKey = isKey,
                        IsFilterable = isFilterable,
                        IsSortable = isSortable,
                        IsFacetable = isFacetable,
                        // IsRetrievable = isRetrievable, // Removed
                    };
                }
                else
                {
                    field = new SimpleField(name, fieldType)
                    {
                        IsKey = isKey,
                        IsFilterable = isFilterable,
                        IsSortable = isSortable,
                        IsFacetable = isFacetable,
                        // IsRetrievable = isRetrievable, // Removed
                    };
                }
            }
            else
            {
                // Handle simple and searchable fields
                var fieldType = ConvertToSearchFieldDataType(dataType);
                if (isSearchable)
                {
                    field = new SearchableField(name, fieldType)
                    {
                        IsKey = isKey,
                        IsFilterable = isFilterable,
                        IsSortable = isSortable,
                        IsFacetable = isFacetable,
                        // IsRetrievable = isRetrievable, // Removed
                    };
                }
                else
                {
                    field = new SimpleField(name, fieldType)
                    {
                        IsKey = isKey,
                        IsFilterable = isFilterable,
                        IsSortable = isSortable,
                        IsFacetable = isFacetable,
                        // IsRetrievable = isRetrievable, // Removed
                    };
                }
            }

            if (isKey && name == "ChunkId")
            {
                field = new SimpleField(name, SearchFieldDataType.String)
                {
                    IsKey = isKey,
                    IsFilterable = isFilterable,
                    IsSortable = isSortable,
                    IsFacetable = isFacetable,
                };
            }

            fields.Add(field);
        }
        return fields;
    }

    // Helper method to convert string to SearchFieldDataType
    private static SearchFieldDataType ConvertToSearchFieldDataType(string dataType)
    {
        return dataType switch
        {
            "Edm.String" => SearchFieldDataType.String,
            "Edm.Int32" => SearchFieldDataType.Int32,
            "Edm.DateTimeOffset" => SearchFieldDataType.DateTimeOffset,
            _ => throw new NotSupportedException($"Data type '{dataType}' is not supported."),
        };
    }

    public static void Main(string[] args)
    {
        Env.Load();

        var requiredVars = new List<string>
        {
            "AISEARCH_INDEXNAME",
            "MODEL_EMBEDDING_DIMENSIONS"
        };

        foreach (var v in requiredVars)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(v)))
            {
                throw new Exception($"Missing required environment variable: {v}");
            }
        }

        string endpoint = Environment.GetEnvironmentVariable("AISEARCH_ENDPOINT");
        string indexName = Environment.GetEnvironmentVariable("AISEARCH_INDEXNAME");

        var credential = new DefaultAzureCredential();
        var indexClient = new SearchIndexClient(new Uri(endpoint), credential);

        // Load fields from JSON
        var fields = LoadFieldsFromJson("aisearch-index.json");

        // Define VectorSearch configuration
        var vectorSearch = new VectorSearch();
        vectorSearch.Algorithms.Add(
            new HnswAlgorithmConfiguration("default-HNSW")
            {
                Parameters = new HnswParameters
                {
                    Metric = VectorSearchAlgorithmMetric.Cosine
                }
            });

        vectorSearch.Algorithms.Add(
            new ExhaustiveKnnAlgorithmConfiguration("default") 
            {
                Parameters = new ExhaustiveKnnParameters
                {
                    Metric = VectorSearchAlgorithmMetric.Cosine
                }
            });

        vectorSearch.Profiles.Add(new VectorSearchProfile("default", "default")); // Provided required parameters
        vectorSearch.Profiles.Add(new VectorSearchProfile("default-HNSW", "default-HNSW")); // Provided required parameters

        var index = new SearchIndex(indexName)
        {
            Fields = fields,
            VectorSearch = vectorSearch
        };

        indexClient.CreateOrUpdateIndex(index);
        Console.WriteLine($"Index '{indexName}' created or updated successfully.");
    }
}