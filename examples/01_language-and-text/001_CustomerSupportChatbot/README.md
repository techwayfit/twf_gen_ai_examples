# Customer Support Chatbot

A multi-turn customer support chatbot built with Blazor and the Twf AI Framework that demonstrates:

- **Conversation memory management** - Maintains context across multiple turns
- **Intent classification** - Understands user sentiment and intent
- **Confidence thresholds** - Escalates to empathetic responses when anger is detected
- **Safety checks** - Validates input before processing
- **Human-in-the-loop design** - Ready for integration with CRM systems

## Features

- ?? **AI-Powered Responses** - Uses OpenAI GPT models for intelligent conversation
- ?? **Real-time Chat** - Interactive Blazor Server chat interface
- ?? **Sentiment Analysis** - Detects angry customers and adjusts response tone
- ??? **Safety Guardrails** - Filters unsafe or inappropriate messages
- ?? **Session Management** - Maintains conversation history per session
- ?? **Bootstrap UI** - Clean, responsive chat widget using Bootstrap 5.3.3 from CDN
- ? **Rich Formatting** - Supports bold, italic, lists, and structured responses

## Project Structure

```
001_CustomerSupportChatbot/
??? Components/
? ??? Pages/
?   ? ??? Home.razor   # Chat interface with rich HTML formatting
?   ?   ??? Error.razor
?   ??? Layout/
?   ?   ??? MainLayout.razor
?   ?   ??? NavMenu.razor
?   ??? App.razor      # Bootstrap CDN configuration
??? Controllers/
?   ??? ChatApiController.cs        # API endpoint for chat
??? wwwroot/         # Static assets
??? Program.cs    # App configuration
??? appsettings.json        # Configuration (API keys)
??? appsettings.Development.json
??? appsettings.local.json  # Local overrides (gitignored)
```

## Setup

### 1. Configure API Key

**Option A: Using appsettings.local.json (Recommended)**

Create `appsettings.local.json` in the project root and add your OpenAI API key:

```json
{
  "OpenAI": {
    "ApiKey": "sk-your-actual-openai-api-key-here"
  }
}
```

This file is automatically excluded from source control via `.gitignore`.

**Option B: Using appsettings.json**

Edit `appsettings.json` and add your OpenAI API key:

```json
{
  "OpenAI": {
    "ApiKey": "sk-your-actual-openai-api-key-here"
  }
}
```

**?? Security Note:** 
- For local development, use `appsettings.local.json` to keep your API key out of source control
- For production, use environment variables or Azure Key Vault instead of hardcoding API keys
- Never commit API keys to version control

### 2. Run the Application

```bash
dotnet run
```

The application will start at `https://localhost:5001` (or the port shown in the console).

### 3. Use the Chat

1. Navigate to the home page
2. Type your message in the input field
3. Press Enter or click Send
4. The bot will respond based on sentiment and safety analysis

## UI Features

### Rich Text Formatting

The chat interface supports rich HTML formatting:

- **Bold text** - Use `**text**` in LLM responses
- *Italic text* - Use `*text*` in LLM responses
- ?? Bullet lists - Auto-formatted with `-` prefix
- ?? Numbered lists - Auto-formatted with `1.`, `2.`, etc.
- ?? Paragraphs - Double line breaks create new paragraphs

### Visual Indicators

- ?? **Color-coded response badges:**
  - ?? Standard - Green badge
  - ?? Escalation - Yellow badge
  - ?? Rejected - Red badge
- ?? **Sentiment icons:**
  - Positive, Neutral, Negative, Angry
- ? **Timestamps** - Each message shows send time
- ?? **Message bubbles** - Different styles for user vs bot

### Dependencies

The application uses Bootstrap 5.3.3 and Bootstrap Icons via CDN:
- **Bootstrap CSS/JS**: https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/
- **Bootstrap Icons**: https://cdn.jsdelivr.net/npm/bootstrap-icons@1.11.3/

No local Bootstrap files are needed - everything loads from CDN for faster updates and smaller repository size.

## How It Works

### Workflow Pipeline

```
User Message
?
Input Validation (length, non-empty)
 ?
Safety Check (LLM)
 ?? SAFE ? Sentiment Analysis (LLM)
    ?  ?? ANGRY ? Escalation Response (empathetic, formatted)
 ?    ?? NORMAL ? Standard Response (helpful, formatted)
    ?? UNSAFE ? Rejection Message
```

### API Endpoint

**POST** `/api/ChatApi/message`

Request:
```json
{
  "sessionId": "optional-session-id",
  "message": "I need help with my order"
}
```

Response:
```json
{
  "sessionId": "unique-session-id",
  "message": "I'd be happy to help you with your order...",
  "responseType": "standard",
  "sentiment": "neutral",
  "timestamp": "2024-01-15T10:30:00Z"
}
```

**DELETE** `/api/ChatApi/session/{sessionId}` - Clear session history

## Customization

### Adjust Response Tone

Edit `ChatApiController.cs` and modify the prompt templates:

```csharp
// For standard responses
promptTemplate: @"
    Help this customer professionally.
 Company: {{company_name}}
    Message: {{user_message}}
    
    Use formatting:
    - Use **bold** for key information
    - Use bullet points (-) for lists
    - Use numbered lists (1., 2., 3.) for steps
"

// For angry customers
promptTemplate: @"
    Customer is angry. Be empathetic and offer concrete help.
  Company: {{company_name}}
    Message: {{user_message}}
    
    Use formatting to make your response clear and caring.
"
```

### Change Sentiment Threshold

Modify the anger detection threshold in `BuildCustomerSupportWorkflow`:

```csharp
.Branch(data => data.GetString("sentiment") == "angry" || 
 data.Get<int>("anger_score") >= 7,  // Change this value (1-10)
```

### Customize UI Styling

The chat interface uses Bootstrap 5.3 classes. You can customize:

1. **Colors** - Modify Bootstrap theme colors in `app.css`
2. **Message styling** - Edit `<style>` section in `Home.razor`
3. **Icons** - Change Bootstrap Icons in the component
4. **Layout** - Adjust container sizes and spacing

### Add More Workflow Nodes

The framework supports:
- **HTTP requests** - Integrate with CRM/ticketing systems
- **Data transforms** - Format/validate data
- **Parallel execution** - Run multiple checks simultaneously
- **Loops** - Process multiple items
- **Conditional branching** - Complex decision trees

See `source/core/README.md` for full documentation.

## Configuration Files

- **appsettings.json** - Base configuration (committed to source control)
- **appsettings.Development.json** - Development-specific settings
- **appsettings.local.json** - Local overrides for sensitive data (gitignored)

The configuration is loaded in order, with later files overriding earlier ones. This means `appsettings.local.json` takes precedence over all other settings files.

## Learning Objectives

This example demonstrates:

1. **Prompt Engineering** - Control LLM tone and persona through system/user prompts
2. **Conversation Memory** - Using `MaintainHistory = true` for multi-turn chat
3. **Intent Classification** - Detecting sentiment and routing accordingly
4. **Confidence Thresholds** - Scoring responses (1-10) for escalation decisions
5. **HITL Design** - Architecture ready for human agent handoff
6. **CRM Integration Points** - Structure for connecting to external systems
7. **Rich UI Formatting** - HTML rendering with markdown-like syntax
8. **Modern Web Development** - CDN-based dependencies, responsive design

## Next Steps

- [ ] Integrate with a real CRM API (Salesforce, HubSpot, etc.)
- [ ] Add database for persistent conversation history
- [ ] Implement human agent handoff
- [ ] Add FAQ matching before LLM call
- [ ] Track order numbers and pull real order data
- [ ] Add authentication and user profiles
- [ ] Deploy to Azure App Service
- [ ] Add file upload support for screenshots
- [ ] Implement typing indicators
- [ ] Add message search/filter

## License

Apache 2.0 - See LICENSE file
