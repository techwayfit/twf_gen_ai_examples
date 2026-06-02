# Embedding Provider Configuration Guide

This document explains how to switch between OpenAI embeddings and local embeddings in your application.

## Overview

The application now supports two embedding providers:

1. **OpenAI** - Uses OpenAI's embedding API (requires API key, cloud-based)
2. **Local** - Uses ElBruno.LocalEmbeddings for offline generation (no API key needed)

## Configuration

### Switch Between Providers

Edit `appsettings.json` or `appsettings.local.json`:

```json
{
  "Embeddings": {
	"Provider": "OpenAI",  // or "Local"
	"EmbeddingDimension": 1536
  }
}
```

### OpenAI Configuration

```json
{
  "Embeddings": {
	"Provider": "OpenAI",
	"EmbeddingDimension": 1536
  },
  "OpenAI": {
	"ApiKey": "your-api-key-here",
	"ChatModel": "gpt-4o-mini",
	"EmbeddingModel": "text-embedding-3-small",
	"Endpoint": "https://api.openai.com/v1"
  }
}
```

**Embedding Dimensions by Model:**
- `text-embedding-3-small`: 1536 dimensions
- `text-embedding-3-large`: 3072 dimensions  
- `text-embedding-ada-002`: 1536 dimensions

### Local Embeddings Configuration

```json
{
  "Embeddings": {
	"Provider": "Local",
	"EmbeddingDimension": 384
  },
  "LocalEmbeddings": {
	"ModelPath": "",
	"EmbeddingDimension": 384
  }
}
```

**Default Model:** `sentence-transformers/all-MiniLM-L6-v2` (384 dimensions)

The model will be automatically downloaded on first use and cached locally.

**Note:** When switching to local embeddings, you must re-index your codebase because the embedding dimensions differ from OpenAI models (384 vs 1536). Existing OpenAI-based indexes are not compatible with local embeddings.

## Implementation Details

### Architecture

The implementation uses the strategy pattern with dependency injection:

1. **IEmbeddingService** - Common interface for all embedding providers
2. **OpenAIEmbeddingService** - OpenAI implementation
3. **LocalEmbeddingService** - Local implementation using ElBruno.LocalEmbeddings

### Key Changes Made

#### 1. New Files Created

- `Services/IEmbeddingService.cs` - Interface abstraction
- `Services/OpenAIEmbeddingService.cs` - OpenAI provider
- `Services/LocalEmbeddingService.cs` - Local provider

#### 2. Modified Files

- `Program.cs` - DI registration based on configuration
- `Services/CodeIndexingWorkflowService.cs` - Uses IEmbeddingService
- `Services/PairProgrammingWorkflowService.cs` - Uses IEmbeddingService  
- `Controllers/PairProgrammerController.cs` - Simplified API calls
- `Services/IndexingJobService.cs` - Removed embeddingModel parameter
- `Models/IndexingJob.cs` - Removed EmbeddingModel property
- `Services/BackgroundIndexingService.cs` - Updated workflow calls
- `036_AI_Pair_Programmer.csproj` - Added ElBruno.LocalEmbeddings package
- `appsettings.json` - Added Embeddings and LocalEmbeddings sections

#### 3. NuGet Package Added

```xml
<PackageReference Include="ElBruno.LocalEmbeddings" Version="1.5.0" />
```

## Benefits

### OpenAI Embeddings

**Pros:**
- Higher quality embeddings
- Larger models available (up to 3072 dimensions)
- Optimized for various tasks

**Cons:**
- Requires API key and internet connection
- Costs money per embedding generated
- Subject to rate limits

### Local Embeddings

**Pros:**
- No API key required
- Completely offline after model download
- No per-use costs
- Fast inference on modern CPUs
- Privacy - data never leaves your machine

**Cons:**
- Lower quality than OpenAI's largest models
- Requires model download (~90-100 MB for default model)
- Fixed model (384 dimensions)
- No multilingual support with default model

## Switching Providers

To switch from OpenAI to Local embeddings:

1. Update `appsettings.local.json`:
   ```json
   {
	 "Embeddings": {
	   "Provider": "Local"
	 }
   }
   ```

2. **Re-index your codebase** - This is required because:
   - Embedding dimensions differ (1536 vs 384)
   - Vector spaces are incompatible
   - Qdrant collections need to be recreated

3. Restart the application

## Advanced: Custom Local Models

If you want to use a different model than the default, you can explore the [ElBruno.LocalEmbeddings documentation](https://github.com/elbruno/elbruno.localembeddings) for alternative models. You would need to:

1. Modify `LocalEmbeddingService.cs` to accept model configuration
2. Update `appsettings.json` with the model name
3. Adjust `EmbeddingDimension` to match your model's output

## Troubleshooting

### Issue: Build errors after adding package

**Solution:** Run `dotnet restore` to download the NuGet package.

### Issue: First run is slow

**Solution:** The local model is being downloaded and cached. This only happens once. Subsequent runs will be fast.

### Issue: Embeddings from different providers don't match

**Solution:** This is expected. OpenAI and local models produce different vector spaces. You must re-index when switching providers.

### Issue: "Model not found" error

**Solution:** Ensure you have internet connection for the first run to download the model. The model will be cached in:
- Windows: `%USERPROFILE%\.cache\huggingface\`
- Linux/Mac: `~/.cache/huggingface/`

## Performance Considerations

### OpenAI
- Network latency: 100-500ms per request
- Rate limits apply
- Can be parallelized up to rate limits

### Local
- In-process: 5-50ms per text (CPU-dependent)
- No rate limits
- Can be parallelized based on CPU cores
- First run downloads model (~90-100 MB)

## Summary

This implementation provides a flexible, pluggable architecture for embedding generation. You can easily switch between cloud-based and local embeddings by changing a single configuration value, making it ideal for:

- **Development:** Use local embeddings to save costs
- **Production:** Use OpenAI for higher quality
- **Air-gapped environments:** Use local embeddings only
- **Hybrid:** Switch based on use case

The abstraction also makes it easy to add new providers (Azure OpenAI, Cohere, etc.) in the future by implementing the `IEmbeddingService` interface.
