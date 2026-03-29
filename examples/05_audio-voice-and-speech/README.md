# Audio, Voice & Speech — Examples #61–68

Applications that process spoken audio, transcribe speech, and generate audio output. These examples connect ASR, TTS, and LLM pipelines for voice-enabled AI applications.

---

## Examples

### #61 — Podcast Chapter & Highlight Generator
Transcribes podcast audio, identifies topic transitions, generates chapter titles and timestamps, extracts quotable highlights, and creates social media clips.

**Key patterns:** `HttpRequestNode` for Whisper/ASR API, `TransformNode` for transcript segmentation, `Workflow.Parallel()` for simultaneous chapter + highlight generation, `OutputParserNode` for structured timestamps.

---

### #62 — Call Center Conversation Analyzer
Transcribes customer calls, analyses agent performance (empathy, compliance, resolution), detects sentiment shifts, and generates QA scorecards.

**Key patterns:** `HttpRequestNode` for ASR + speaker diarisation API, `Workflow.ForEach()` over conversation turns, `OutputParserNode` for QA scorecard extraction, `ConditionNode` for coaching-opportunity flagging.

---

### #63 — Accent-Aware Customer Service Voice Bot
Handles customer queries over phone with multi-accent ASR, context-aware responses, and graceful human-agent handoff.

**Key patterns:** `HttpRequestNode` for telephony ASR API (Twilio), multi-turn `LlmNode` with `MaintainHistory`, `ConditionNode` + `Workflow.Branch()` for human handoff, `HttpRequestNode` for TTS response.

---

### #64 — Audio Content Repurposer
Ingests audio (interviews, webinars) and generates blog posts, LinkedIn articles, tweet threads, and slide decks from a single source.

**Key patterns:** `HttpRequestNode` for transcription, `Workflow.Parallel()` to generate multiple formats simultaneously, `OutputParserNode` per format, `PromptBuilderNode` with format-specific templates.

---

### #65 — Real-Time Lecture Transcription & Note Taker
Transcribes lectures in real-time, organises content by concept, generates structured notes, creates flashcards, and produces a study guide.

**Key patterns:** `HttpRequestNode` for streaming ASR API, `TransformNode` for concept segmentation, `Workflow.Parallel()` for notes + flashcard generation, `MergeNode` for study guide assembly.

---

### #66 — Music Mood Analyzer & Playlist Curator
Analyses audio tracks for mood, energy, and genre, maps them to emotional states, and generates personalised playlists with explanations.

**Key patterns:** `HttpRequestNode` for audio feature extraction API, `EmbeddingNode` for mood similarity, `OutputParserNode` for mood classification, `LlmNode` for playlist rationale generation.

---

### #67 — Multilingual Meeting Interpreter
Transcribes speech in multiple languages simultaneously, translates in real-time, maintains speaker attribution, and generates a multilingual transcript.

**Key patterns:** `Workflow.Parallel()` for per-speaker ASR streams, `LlmNode` for translation per turn, `MergeNode` with speaker attribution, `HttpRequestNode` for real-time translation API.

---

### #68 — Audiobook Producer from Text
Converts written content to professional audiobooks with natural prosody, consistent voice, and chapter-by-chapter audio generation.

**Key patterns:** `TransformNode` for chapter segmentation, `Workflow.ForEach()` over chapters, `HttpRequestNode` for TTS API (ElevenLabs/Azure), `DelayNode.RateLimitDelay()` for API limits.

---

## TwfAiFramework Quickstart for This Category

```csharp
// Typical pattern: transcribe audio → process transcript → generate output
var result = await Workflow.Create("AudioPipeline")
    .UseLogger(logger)
    // Step 1: Transcribe audio via Whisper API
    .AddNode(new HttpRequestNode("Transcribe", new HttpRequestConfig
    {
        Method      = "POST",
        UrlTemplate = "https://api.openai.com/v1/audio/transcriptions",
        Headers     = new()
        {
            ["Authorization"] = $"Bearer {Environment.GetEnvironmentVariable("OPENAI_API_KEY")}"
        }
        // Note: multipart/form-data audio upload — wire via custom BaseNode subclass for binary uploads
    }))
    // Step 2: Extract transcript text
    .AddNode(new TransformNode("ExtractTranscript", data =>
        data.Clone().Set("transcript", data.Get<string>("http_response"))))
    // Step 3: Generate summary + action items in parallel
    .Parallel(
        new PromptBuilderNode("Summarise this transcript into key topics:\n{{transcript}}"),
        new PromptBuilderNode("Extract all action items from this transcript:\n{{transcript}}")
    )
    // Step 4: Parse structured output
    .AddNode(new OutputParserNode())
    .RunAsync(new WorkflowData()
        .Set("audio_url", "https://example.com/meeting-recording.mp3"));
```
