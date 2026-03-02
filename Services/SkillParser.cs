using System.Text.RegularExpressions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace AgentControlPanel.Services;

public class SkillFrontmatter
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? License { get; set; }
    public string? Compatibility { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
    [YamlMember(Alias = "allowed-tools")]
    public string? AllowedTools { get; set; }
}

public class SkillParseResult
{
    public bool IsValid { get; set; }
    public SkillFrontmatter Frontmatter { get; set; } = new();
    public string Body { get; set; } = string.Empty;
    public List<string> Errors { get; set; } = new();
}

public class SkillNameValidation
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
}

public interface ISkillParser
{
    SkillParseResult Parse(string skillMd);
    SkillNameValidation ValidateName(string name);
    string SanitizeNameForToolUse(string name);
}

public class SkillParser : ISkillParser
{
    private static readonly Regex FrontmatterRegex = new(
        @"^---\s*\n(.*?)\n---\s*\n?",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex NameRegex = new(
        @"^[a-z0-9]([a-z0-9-]*[a-z0-9])?$",
        RegexOptions.Compiled);

    private readonly IDeserializer _yamlDeserializer;

    public SkillParser()
    {
        _yamlDeserializer = new DeserializerBuilder()
            .WithNamingConvention(HyphenatedNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
    }

    public SkillParseResult Parse(string skillMd)
    {
        var result = new SkillParseResult();

        if (string.IsNullOrWhiteSpace(skillMd))
        {
            result.Errors.Add("SKILL.md content is empty.");
            return result;
        }

        var match = FrontmatterRegex.Match(skillMd);
        if (!match.Success)
        {
            result.Errors.Add("No YAML frontmatter found. SKILL.md must start with '---' delimited YAML.");
            return result;
        }

        var yamlContent = match.Groups[1].Value;
        result.Body = skillMd.Substring(match.Length).Trim();

        try
        {
            result.Frontmatter = _yamlDeserializer.Deserialize<SkillFrontmatter>(yamlContent) ?? new SkillFrontmatter();
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Failed to parse YAML frontmatter: {ex.Message}");
            return result;
        }

        var nameValidation = ValidateName(result.Frontmatter.Name);
        if (!nameValidation.IsValid)
            result.Errors.AddRange(nameValidation.Errors);

        if (string.IsNullOrWhiteSpace(result.Frontmatter.Description))
            result.Errors.Add("'description' is required and must not be empty.");

        if (result.Frontmatter.Description.Length > 1024)
            result.Errors.Add($"'description' must be at most 1024 characters (currently {result.Frontmatter.Description.Length}).");

        if (result.Frontmatter.Compatibility?.Length > 500)
            result.Errors.Add($"'compatibility' must be at most 500 characters (currently {result.Frontmatter.Compatibility.Length}).");

        result.IsValid = result.Errors.Count == 0;
        return result;
    }

    public SkillNameValidation ValidateName(string name)
    {
        var validation = new SkillNameValidation { IsValid = true };

        if (string.IsNullOrWhiteSpace(name))
        {
            validation.IsValid = false;
            validation.Errors.Add("'name' is required and must not be empty.");
            return validation;
        }

        if (name.Length > 64)
        {
            validation.IsValid = false;
            validation.Errors.Add($"'name' must be at most 64 characters (currently {name.Length}).");
        }

        if (!NameRegex.IsMatch(name))
        {
            validation.IsValid = false;
            validation.Errors.Add("'name' must contain only lowercase letters, numbers, and hyphens, and must not start or end with a hyphen.");
        }

        if (name.Contains("--"))
        {
            validation.IsValid = false;
            validation.Errors.Add("'name' must not contain consecutive hyphens ('--').");
        }

        if (name.Contains("anthropic") || name.Contains("claude"))
        {
            validation.IsValid = false;
            validation.Errors.Add("'name' must not contain reserved words 'anthropic' or 'claude'.");
        }

        return validation;
    }

    public string SanitizeNameForToolUse(string name)
    {
        return name.Replace("-", "_");
    }
}
