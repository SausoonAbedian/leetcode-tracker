using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic;
using Beta = Anthropic.Models.Beta.Messages;

namespace Tracker.Api.Services.Ai;

public record AiReply(string Text, string Provider, string Model);

public class AiException(string message) : Exception(message);

public interface IAiProvider
{
    string Id { get; }
    string DisplayName { get; }
    bool IsConfigured { get; }
    string ConfigHint { get; }
    Task<AiReply> CompleteAsync(string system, string user, AiSettings settings, CancellationToken ct);
}

/// <summary>Claude through the official Anthropic C# SDK (ANTHROPIC_API_KEY).</summary>
public class AnthropicProvider(IConfiguration config) : IAiProvider
{
    public string Id => "anthropic";
    public string DisplayName => "Anthropic API (Claude)";
    string? Key => config["ANTHROPIC_API_KEY"];
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Key);
    public string ConfigHint => "Set ANTHROPIC_API_KEY in .env";

    public async Task<AiReply> CompleteAsync(string system, string user, AiSettings settings, CancellationToken ct)
    {
        if (!IsConfigured) throw new AiException("Anthropic API key is not configured. " + ConfigHint);
        var model = string.IsNullOrWhiteSpace(settings.AnthropicModel) ? "claude-opus-5-5" : settings.AnthropicModel.Trim();
        AnthropicClient client = new() { ApiKey = Key };
        // Beta endpoint so a safety-classifier refusal is retried server-side on a fallback model ("default" routing).
        var response = await client.Beta.Messages.Create(new Beta.MessageCreateParams
        {
            Model = model,
            MaxTokens = 16000,
            System = system,
            Betas = ["server-side-fallback-2026-07-01"],
            Fallbacks = new Beta.Default(),
            Messages = [new Beta.BetaUserMessageParam { Content = user }],
        }, ct);

        if (response.StopReason?.ToString()?.Contains("refusal", StringComparison.OrdinalIgnoreCase) == true)
            throw new AiException("Claude declined this request. Try rephrasing or another provider.");
        var text = string.Join("\n", response.Content.Select(b => b.Value).OfType<Beta.BetaTextBlock>().Select(t => t.Text));
        if (string.IsNullOrWhiteSpace(text)) throw new AiException("Claude returned an empty answer.");
        return new AiReply(text, Id, model);
    }
}

/// <summary>DeepSeek's OpenAI-compatible chat completions API (DEEPSEEK_API_KEY).</summary>
public class DeepSeekProvider(IConfiguration config, IHttpClientFactory http) : IAiProvider
{
    public string Id => "deepseek";
    public string DisplayName => "DeepSeek API";
    string? Key => config["DEEPSEEK_API_KEY"];
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Key);
    public string ConfigHint => "Set DEEPSEEK_API_KEY in .env";

    public async Task<AiReply> CompleteAsync(string system, string user, AiSettings settings, CancellationToken ct)
    {
        if (!IsConfigured) throw new AiException("DeepSeek API key is not configured. " + ConfigHint);
        var model = string.IsNullOrWhiteSpace(settings.DeepSeekModel) ? "deepseek-chat" : settings.DeepSeekModel.Trim();
        var baseUrl = (config["DEEPSEEK_BASE_URL"] ?? "https://api.deepseek.com").TrimEnd('/');
        var client = http.CreateClient("ai");
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/chat/completions");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Key);
        var body = new JsonObject
        {
            ["model"] = model,
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = system },
                new JsonObject { ["role"] = "user", ["content"] = user }),
            ["stream"] = false
        };
        req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var resp = await client.SendAsync(req, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new AiException($"DeepSeek returned {(int)resp.StatusCode}: {Truncate(json, 300)}");
        var node = JsonNode.Parse(json);
        var text = node?["choices"]?[0]?["message"]?["content"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(text)) throw new AiException("DeepSeek returned an empty answer.");
        return new AiReply(text, Id, model);
    }

    static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "…";
}

/// <summary>Claude Code CLI in print mode (CLAUDE_CODE_OAUTH_TOKEN from `claude setup-token`, or ANTHROPIC_API_KEY).</summary>
public class ClaudeCodeProvider(IConfiguration config) : IAiProvider
{
    public string Id => "claudecode";
    public string DisplayName => "Claude Code CLI";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(config["CLAUDE_CODE_OAUTH_TOKEN"]);
    public string ConfigHint => "Run `claude setup-token` on your PC and set CLAUDE_CODE_OAUTH_TOKEN in .env";

    public async Task<AiReply> CompleteAsync(string system, string user, AiSettings settings, CancellationToken ct)
    {
        if (!IsConfigured) throw new AiException("Claude Code is not configured. " + ConfigHint);
        var cwd = Path.Combine(Path.GetTempPath(), "tracker-claude");
        Directory.CreateDirectory(cwd);   // empty folder: the CLI has nothing to read or change

        var psi = new ProcessStartInfo(config["CLAUDE_CLI"] ?? "claude")
        {
            WorkingDirectory = cwd, RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false
        };
        foreach (var a in new[] { "-p", "--output-format", "json", "--append-system-prompt", system,
                     "--disallowedTools", "Bash,Edit,Write,NotebookEdit,WebFetch,WebSearch" })
            psi.ArgumentList.Add(a);
        if (!string.IsNullOrWhiteSpace(settings.ClaudeCodeModel)) { psi.ArgumentList.Add("--model"); psi.ArgumentList.Add(settings.ClaudeCodeModel.Trim()); }
        psi.Environment["CLAUDE_CODE_OAUTH_TOKEN"] = config["CLAUDE_CODE_OAUTH_TOKEN"];
        psi.Environment.Remove("ANTHROPIC_API_KEY");   // make the CLI use the subscription token, not API billing

        using var proc = new Process { StartInfo = psi };
        try { proc.Start(); }
        catch (Exception ex) { throw new AiException($"Could not start the Claude Code CLI: {ex.Message}"); }
        await proc.StandardInput.WriteAsync(user);
        proc.StandardInput.Close();
        var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMinutes(5));
        try { await proc.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException) { proc.Kill(true); throw new AiException("Claude Code timed out."); }
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        try
        {
            var node = JsonNode.Parse(stdout);
            if (node?["is_error"]?.GetValue<bool>() == true)
                throw new AiException("Claude Code error: " + (node["result"]?.ToString() ?? stderr));
            var text = node?["result"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(text)) return new AiReply(text, Id, settings.ClaudeCodeModel is { Length: > 0 } m ? m : "default");
        }
        catch (JsonException) { }
        throw new AiException($"Claude Code failed (exit {proc.ExitCode}): {(stderr + stdout).Trim()[..Math.Min(400, (stderr + stdout).Trim().Length)]}");
    }
}
