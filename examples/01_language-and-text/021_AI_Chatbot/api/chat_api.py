from fastapi import APIRouter
from fastapi.responses import StreamingResponse

from api.schemas import ChatRequest
from services.ai_message_service import AIMessageService
from tools.compact_chat import ConversationManager
from tools.fetch_web_link import extract_urls, fetch_content
from tools.multi_modal import build_user_content
from sse import sse_event


router = APIRouter(prefix="/api/chat", tags=["chat"])
message_service = AIMessageService()
conversation_mgr = ConversationManager()

DEFAULT_SYSTEM_PROMPT = "You are a concise and helpful AI assistant. When the user shares a URL, read its content and answer based on it."


@router.post("/stream")
def stream_chat(req: ChatRequest) -> StreamingResponse:
    def event_generator():
        session_id = req.session_id
        system_prompt = req.system_prompt or DEFAULT_SYSTEM_PROMPT

        yield sse_event("status", {"message": "Starting chat stream..."})

        try:
            # --- fetch_web_link tool: detect URLs and fetch content ---
            urls = extract_urls(req.message)
            user_msg = req.message
            for url in urls:
                yield sse_event("status", {"message": f"Fetching content from {url}..."})
                content = fetch_content(url)
                if content and not content.startswith("[Error"):
                    user_msg += f"\n\nContent from {url}:\n{content}"

            # --- multi_modal tool: build user content with optional image ---
            user_content = build_user_content(user_msg, req.image_data, req.image_type)

            # --- compact_chat tool: build context with conversation history ---
            if session_id:
                messages = conversation_mgr.build_context(session_id, system_prompt, user_content)
            else:
                messages = [
                    {"role": "system", "content": system_prompt},
                    {"role": "user", "content": user_content},
                ]

            # --- stream tokens ---
            full_response = ""
            for token in message_service.stream_messages(messages):
                full_response += token
                yield sse_event("token", {"text": token})

            yield sse_event("status", {"message": "Chat stream completed."})

            # --- store conversation turn and compact if needed ---
            if session_id:
                conversation_mgr.add_turn(session_id, req.message, full_response)
                if conversation_mgr.needs_compact(session_id):
                    yield sse_event("status", {"message": "Compacting conversation history..."})
                    conversation_mgr.compact(session_id)

            yield sse_event("done", {"ok": True, "full_response": full_response})

        except Exception as ex:
            yield sse_event("error", {"message": str(ex)})

    return StreamingResponse(
        event_generator(),
        media_type="text/event-stream",
        headers={
            "Cache-Control": "no-cache",
            "Connection": "keep-alive",
            "X-Accel-Buffering": "no",
        },
    )
