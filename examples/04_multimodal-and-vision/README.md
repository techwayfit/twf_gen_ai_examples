# Multimodal & Vision Applications — Examples #51–60

Applications that combine visual inputs (images, photos, satellite data) with language models. These examples leverage Vision-Language Models (VLMs) for real-world visual understanding tasks.

---

## Examples

### #51 — E-Commerce Product Image Analyzer & Tagger
Ingests product images, auto-generates titles, descriptions, and attribute tags, detects image quality issues, and classifies products into category taxonomies.

**Key patterns:** `HttpRequestNode` for VLM API (image → structured analysis), `OutputParserNode` with `fieldMapping` for tags/description/category, `ConditionNode` for quality-gate routing.

---

### #52 — Medical Image Report Assistant
Analyses medical images (X-rays, MRI, CT), generates preliminary radiologist-style reports, flags areas of concern, and compares against prior studies.

**Key patterns:** `HttpRequestNode` for medical VLM API, `OutputParserNode` for structured clinical report, `ConditionNode` for concern-severity routing, `Workflow.Branch()` for escalation.

---

### #53 — Real Estate Property Analyzer
Analyses property photos, estimates condition and renovation needs, detects features, identifies defects, and provides renovation cost estimates.

**Key patterns:** `Workflow.Parallel()` over multiple property photos, `OutputParserNode` for feature and defect extraction, `MergeNode` to aggregate multi-image results, `LlmNode` for cost estimation.

---

### #54 — Brand Asset Compliance Checker
Checks design assets against brand guidelines — verifying logo placement, colour palette, typography, and spacing standards.

**Key patterns:** `HttpRequestNode` for VLM API with brand guidelines in system prompt, `OutputParserNode` for structured compliance violations, `Workflow.ForEach()` over asset collection.

---

### #55 — Content Moderation System
Analyses user-generated content (images, text, video thumbnails) for policy violations, assigning categories, severity, and explainable decisions.

**Key patterns:** `Workflow.Parallel()` for simultaneous text + image analysis, `OutputParserNode` for violation category + severity, `ConditionNode` for auto-remove vs. human-review routing.

---

### #56 — Restaurant Menu Analyzer & Nutrition Estimator
Analyses menu photos, identifies dishes, estimates nutrition, detects allergens, and generates personalised meal recommendations.

**Key patterns:** `HttpRequestNode` for food-recognition VLM API, `OutputParserNode` for dish + nutrition extraction, `ConditionNode` for dietary-constraint filtering.

---

### #57 — Manufacturing Defect Detector
Analyses product images from manufacturing lines, classifies defects, provides severity scores, and generates quality reports.

**Key patterns:** `Workflow.ForEach()` over inspection images, `HttpRequestNode` for vision API, `OutputParserNode` for defect classification + severity, `ConditionNode` for reject/pass routing.

---

### #58 — Plant Disease & Agricultural Advisor
Analyses crop photos, identifies diseases and nutrient deficiencies, assesses spread risk, and recommends treatments informed by weather data.

**Key patterns:** `Workflow.Parallel()` for image analysis + weather API fetch, `MergeNode` to combine results, `OutputParserNode` for disease + treatment extraction.

---

### #59 — Handwritten Homework Grader
Processes photos of handwritten student work, transcribes content, grades with partial credit, provides feedback on errors, and tracks progress.

**Key patterns:** `HttpRequestNode` for handwriting OCR/VLM API, `OutputParserNode` for transcribed answers, `LlmNode` with grading rubric in system prompt, `WorkflowContext` global state for progress tracking.

---

### #60 — Satellite Image Change Detector
Analyses satellite imagery over time to detect changes (construction, deforestation, flooding) and generates change reports.

**Key patterns:** `Workflow.Parallel()` for before/after image analysis, `LlmNode` for change comparison reasoning, `OutputParserNode` for structured change report, `HttpRequestNode` for alert dispatch.

---

## TwfAiFramework Quickstart for This Category

```csharp
// Typical pattern: submit image to vision API → parse structured output
var result = await Workflow.Create("ImageAnalysis")
    .UseLogger(logger)
    // Call vision-language model with image URL
    .AddNode(new HttpRequestNode("VisionAPI", new HttpRequestConfig
    {
        Method      = "POST",
        UrlTemplate = "https://api.openai.com/v1/chat/completions",
        Headers     = new()
        {
            ["Authorization"] = $"Bearer {Environment.GetEnvironmentVariable("OPENAI_API_KEY")}"
        },
        Body = new
        {
            model    = "gpt-4o",
            messages = new[]
            {
                new
                {
                    role    = "user",
                    content = new object[]
                    {
                        new { type = "text",      text      = "{{analysis_prompt}}" },
                        new { type = "image_url", image_url = new { url = "{{image_url}}" } }
                    }
                }
            },
            response_format = new { type = "json_object" }
        }
    }))
    // Extract structured result
    .AddNode(new OutputParserNode())
    .RunAsync(new WorkflowData()
        .Set("image_url",       "https://example.com/product-image.jpg")
        .Set("analysis_prompt", "Analyse this product image. Return JSON with: title, description, tags[], quality_score (0-10), defects[]."));
```
