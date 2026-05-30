using _036_AI_Pair_Programmer.Models;
using _036_AI_Pair_Programmer.Services;
using Microsoft.AspNetCore.Mvc;

namespace _036_AI_Pair_Programmer.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class PairProgrammerController(
    IConfiguration configuration,
    CodeIndexingWorkflowService indexingService,
    PairProgrammingWorkflowService pairProgrammingService) : ControllerBase
{
    [HttpPost("index")]
    public async Task<ActionResult<IndexResult>> IndexRepository([FromBody] IndexRequest request, CancellationToken ct)
    {
        var apiKey = configuration["OpenAI:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey == "your-openai-api-key-here")
        {
            return BadRequest(new { error = Constants.Messages.OpenAiKeyNotConfigured });
        }

        if (string.IsNullOrWhiteSpace(request.RepoPath))
        {
            return BadRequest(new { error = Constants.Messages.RepoPathRequired });
        }

        var fullPath = Path.GetFullPath(request.RepoPath.Trim());
        if (!Directory.Exists(fullPath))
        {
            return NotFound(new { error = Constants.Messages.RepoPathNotFound });
        }

        var model = configuration["OpenAI:EmbeddingModel"] ?? "text-embedding-3-small";
        var endpoint = configuration["OpenAI:Endpoint"] ?? "https://api.openai.com/v1";

        var sanitized = new IndexRequest
        {
            RepoPath = fullPath,
            Languages = request.Languages,
            MaxChunkTokens = Math.Clamp(request.MaxChunkTokens, 200, 2_000),
            MaxFiles = Math.Clamp(request.MaxFiles, 1, 2_000)
        };

        var result = await indexingService.RunAsync(sanitized, apiKey, model, endpoint, ct);
        return Ok(result);
    }

    [HttpPost("query")]
    public async Task<ActionResult<PairProgrammerResult>> Query([FromBody] QueryRequest request, CancellationToken ct)
    {
        var apiKey = configuration["OpenAI:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey == "your-openai-api-key-here")
        {
            return BadRequest(new { error = Constants.Messages.OpenAiKeyNotConfigured });
        }

        if (string.IsNullOrWhiteSpace(request.RepoPath))
        {
            return BadRequest(new { error = Constants.Messages.RepoPathRequired });
        }

        if (string.IsNullOrWhiteSpace(request.UserRequest))
        {
            return BadRequest(new { error = Constants.Messages.UserRequestRequired });
        }

        var fullPath = Path.GetFullPath(request.RepoPath.Trim());
        if (!Directory.Exists(fullPath))
        {
            return NotFound(new { error = Constants.Messages.RepoPathNotFound });
        }

        var chatModel = configuration["OpenAI:ChatModel"] ?? "gpt-4o-mini";
        var embeddingModel = configuration["OpenAI:EmbeddingModel"] ?? "text-embedding-3-small";
        var endpoint = configuration["OpenAI:Endpoint"] ?? "https://api.openai.com/v1";

        var sanitized = new QueryRequest
        {
            RepoPath = fullPath,
            UserRequest = request.UserRequest.Trim(),
            TopK = Math.Clamp(request.TopK, 1, 20),
            TaskType = string.IsNullOrWhiteSpace(request.TaskType) ? "implement" : request.TaskType.Trim().ToLowerInvariant()
        };

        try
        {
            var result = await pairProgrammingService.RunAsync(
                sanitized,
                apiKey,
                chatModel,
                embeddingModel,
                endpoint,
                ct);

            return Ok(result);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains(Constants.Messages.IndexNotFound, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { error = Constants.Messages.IndexNotFound });
        }
    }
}
