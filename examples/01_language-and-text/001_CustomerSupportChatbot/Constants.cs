using static System.Runtime.InteropServices.JavaScript.JSType;

namespace _001_CustomerSupportChatbot
{
    public static class Constants
    {
        public const string CompanyName = "TechWayFit Inc.";
        public const string DefaultResponseType = "standard";

        public static class Prompts
        {
            public const string BasicSupportSystemTemplate = "Provide a clear, concise, and helpful response.";
            public const string BasicSupportPrompt = "You are a helpful customer support agent for {{company_name}}. Answer the user's question based on the information provided.Message: {{user_message}}. If you don't know the answer, say you don't know but offer to connect them with support.";

            public const string SafetyCheckSystemPrompt = "You are a content safety classifier. Be concise.";
            public const string SafetyCheckPrompt= @"Classify if this customer message is safe to respond to. Message: ""{{user_message}}"" Respond ONLY with JSON: {""is_safe"": true/false, ""reason"": ""brief reason""}";

            public const string UnSafeResponse = "I'm unable to process that request. Please contact **support@techflow.com** for assistance.";

            public const string SentimentPrompt = @"Analyze the sentiment: ""{{user_message}}"".JSON: {""sentiment"": ""positive|neutral|negative|angry"", ""score"": 1-10}";
            public const string SentimentEscalationSystemPrompt = "You are an empathetic senior support agent. Format responses clearly with markdown-like syntax.";
            public const string SentimentNormalSystemPrompt = "You are a helpful support agent for {{company_name}}. Format responses clearly with markdown-like syntax.";
            public const string SentimentEscalationPrompt = @"
Customer is angry. Be empathetic and offer concrete help.
Company: {{company_name}}
Message: {{user_message}}

Provide a warm, empathetic response. Use formatting:
- Use **bold** for important points
- Use bullet points (-) to list action items or options
- Be conversational and understanding
- Offer to escalate if needed

Format your response in a clear, well-structured way.";



            public const string SentimentNormalPrompt = @"
Help this customer professionally.
Company: {{company_name}}
Message: {{user_message}}

Provide a helpful, professional response. Use formatting:
- Use **bold** for key information or product names
- Use bullet points (-) to list features, steps, or options
- Use numbered lists (1., 2., 3.) for sequential instructions
- Keep it conversational and friendly

Format your response in a clear, well-structured way.";

  //          public const string EscalationPrompt = "You are a senior support agent. The customer's issue may require escalation. Provide a warm, empathetic response and offer to escalate if needed.";

        }

        public static class Messages
        {
            public const string EmptyMessage = "Message cannot be empty";
            public const string SessionCleared = "Session cleared";
            public const string SessionNotFound = "Session not found";
            public const string OpenApiKeyNotConfigured = "OpenAI API key not configured";
            public const string RequestCouldNotProcessed = "I apologize, I couldn't process that request.";
            public const string FailedToProcessRequest = "Failed to process your message. Please try again.";
        }
    }
}
