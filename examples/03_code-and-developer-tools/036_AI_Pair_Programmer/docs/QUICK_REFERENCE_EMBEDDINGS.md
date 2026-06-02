# Embedding Configuration Quick Reference

## Switch Provider in 30 Seconds

### Option 1: OpenAI (Cloud)

**appsettings.local.json**:
```json
{
  "Embeddings": { "Provider": "OpenAI", "EmbeddingDimension": 1536 },
  "OpenAI": { "ApiKey": "sk-proj-YOUR-KEY-HERE" }
}
```

✅ High quality | ❌ Costs money | ❌ Requires internet

---

### Option 2: Local (Offline)

**appsettings.local.json**:
```json
{
  "Embeddings": { "Provider": "Local", "EmbeddingDimension": 384 }
}
```

✅ Free | ✅ Fast | ✅ Private | ✅ Offline

---

## ⚠️ When Switching: RE-INDEX YOUR CODE!

Different providers = different dimensions = incompatible vectors

---

## Common Configuration Patterns

### Development (Fast & Free)
```json
{ "Embeddings": { "Provider": "Local" } }
```

### Production (Max Quality)
```json
{ "Embeddings": { "Provider": "OpenAI" } }
```

### High Privacy
```json
{ "Embeddings": { "Provider": "Local" } }
```

### Air-Gapped Environment
```json
{ "Embeddings": { "Provider": "Local" } }
```

---

## Model Options

| Model | Provider | Dimensions | Quality | Cost |
|-------|----------|-----------|---------|------|
| text-embedding-3-small | OpenAI | 1536 | ⭐⭐⭐⭐⭐ | $ |
| text-embedding-3-large | OpenAI | 3072 | ⭐⭐⭐⭐⭐ | $$$ |
| all-MiniLM-L6-v2 | Local | 384 | ⭐⭐⭐⭐ | Free |

---

## Performance at a Glance

```
Time per Embedding:
OpenAI: [████████████████████░░] 150-400ms
Local:  [██░░░░░░░░░░░░░░░░░░░░] 5-20ms

Cost for 500 Embeddings:
OpenAI: $0.001 - $0.01
Local:  $0.00
```

---

## Troubleshooting One-Liners

| Problem | Solution |
|---------|----------|
| "API key not configured" | Set `OpenAI:ApiKey` in appsettings.local.json |
| "Rate limit exceeded" | Switch to Local or wait 60 seconds |
| "Model not found" | First run downloads model (requires internet) |
| Search quality poor | Check if you re-indexed after switching providers |
| Slow first run | Local model is downloading (~100MB) |

---

## File Locations

**Configuration**: 
- `appsettings.json` (defaults)
- `appsettings.local.json` (your overrides) ⭐ EDIT THIS

**Local Model Cache**:
- Windows: `%USERPROFILE%\.cache\huggingface\`
- Mac/Linux: `~/.cache/huggingface/`

**Full Documentation**:
- [Complete Guide](LOCAL_VS_API_EMBEDDINGS.md)
- [Technical Details](EMBEDDING_PROVIDER_CONFIGURATION.md)

---

## Need Help?

- Read the [full comparison guide](LOCAL_VS_API_EMBEDDINGS.md)
- Check [troubleshooting section](LOCAL_VS_API_EMBEDDINGS.md#troubleshooting)
- Open an [issue on GitHub](https://github.com/techwayfit/twf_gen_ai_examples/issues)

---

**TL;DR**: Local = Fast & Free | OpenAI = Best Quality
**Default**: OpenAI (change `Provider` to "Local" for offline use)
**Remember**: Always re-index when switching providers!
