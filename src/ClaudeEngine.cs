using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta;
using Anthropic.Models.Beta.Messages;

namespace ESPLanguageEN;

// "Claude (API key)" engine: one Messages API call per batch of names (official Anthropic C# SDK)
static class ClaudeEngine
{
    public static readonly string[] Models = { "claude-opus-5-5", "claude-sonnet-5-5", "claude-haiku-5-5" };
    public const string DefaultModel = "claude-opus-5-5";

    public static async Task<string> Ask(string apiKey, string model, string system, string user)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Claude needs an API key: console.anthropic.com -> API keys, then paste it in Settings.");
        model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim();
        var client = new AnthropicClient { ApiKey = apiKey.Trim() };

        // Haiku has no server-side refusal fallback; Opus / Sonnet re-serve a refused request on another model
        bool fallback = !model.StartsWith("claude-haiku");
        var p = new MessageCreateParams
        {
            Model = model,
            MaxTokens = 16000,
            System = system,
            OutputConfig = new BetaOutputConfig { Effort = Effort.Low },   // short, simple task
            Messages = [new BetaUserMessageParam { Content = user }],
            Betas = fallback ? [AnthropicBeta.ServerSideFallback2026_07_01] : null,
            Fallbacks = fallback ? new Default() : null,
        };
        BetaMessage resp;
        try { resp = await client.Beta.Messages.Create(p); }
        catch (AnthropicUnauthorizedException) { throw new InvalidOperationException("Claude: the API key is wrong or revoked (401). Check it in Settings."); }
        catch (AnthropicForbiddenException) { throw new InvalidOperationException("Claude: this key may not use " + model + " (403)."); }
        catch (AnthropicNotFoundException) { throw new InvalidOperationException("Claude: unknown model \"" + model + "\" (404). Pick one in Settings."); }
        catch (AnthropicRateLimitException) { throw new InvalidOperationException("Claude: rate limit or credit limit reached (429). Wait a minute or check console.anthropic.com → Billing."); }
        catch (Anthropic5xxException) { throw new InvalidOperationException("Claude: the API is busy or down (5xx). Try again later; translated names so far are kept."); }
        catch (AnthropicIOException ex) { throw new InvalidOperationException("Claude: no connection to api.anthropic.com: " + ex.Message); }
        catch (AnthropicApiException ex) { throw new InvalidOperationException("Claude API error: " + ex.Message); }

        if (resp.StopReason == "refusal")
            throw new InvalidOperationException("Claude declined this batch" +
                (resp.StopDetails is { } d && !string.IsNullOrEmpty(d.Explanation) ? ": " + d.Explanation : "."));
        var text = string.Concat(resp.Content.Select(b => b.TryPickText(out var t) ? t.Text : ""));
        if (resp.StopReason == "max_tokens" && !text.TrimEnd().EndsWith("]"))
            throw new InvalidOperationException("Claude's answer was cut off (max_tokens).");
        return text;
    }
}
