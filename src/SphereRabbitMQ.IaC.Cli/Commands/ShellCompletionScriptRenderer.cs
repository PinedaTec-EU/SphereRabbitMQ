using System.Text;

namespace SphereRabbitMQ.IaC.Cli.Commands;

internal static class ShellCompletionScriptRenderer
{
    private static readonly IReadOnlyDictionary<string, string[]> CommandOptions = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["init"] = ["--template", "--output-file", "--force"],
        ["validate"] = ["--file", "--output", "--verbose"],
        ["plan"] = ["--file", "--output", "--management-url", "--username", "--password", "--vhost", "--verbose"],
        ["apply"] = ["--file", "--output", "--management-url", "--username", "--password", "--vhost", "--dry-run", "--migrate", "--verbose"],
        ["destroy"] = ["--file", "--output", "--management-url", "--username", "--password", "--vhost", "--dry-run", "--verbose", "--allow-destructive", "--auto-approve"],
        ["purge"] = ["--file", "--output", "--management-url", "--username", "--password", "--vhost", "--dry-run", "--verbose", "--allow-destructive", "--auto-approve"],
        ["export"] = ["--file", "--output", "--management-url", "--username", "--password", "--vhost", "--output-file", "--include-broker", "--verbose"],
        ["completion"] = [],
    };

    internal static string Render(string shell, IReadOnlyList<string> templateNames)
        => shell switch
        {
            "bash" => RenderBash(templateNames),
            "zsh" => RenderZsh(templateNames),
            "pwsh" => RenderPwsh(templateNames),
            _ => throw new InvalidOperationException("Unsupported shell. Use 'bash', 'zsh', or 'pwsh'."),
        };

    private static string RenderBash(IReadOnlyList<string> templateNames)
    {
        var templateList = string.Join(" ", templateNames);
        var commandList = string.Join(" ", CommandOptions.Keys);
        var builder = new StringBuilder();
        builder.AppendLine("_sprmq_completion() {");
        builder.AppendLine("  local cur prev command");
        builder.AppendLine("  COMPREPLY=()");
        builder.AppendLine("  cur=\"${COMP_WORDS[COMP_CWORD]}\"");
        builder.AppendLine("  prev=\"${COMP_WORDS[COMP_CWORD-1]}\"");
        builder.AppendLine("  command=\"${COMP_WORDS[1]}\"");
        builder.AppendLine();
        builder.AppendLine("  if [[ ${COMP_CWORD} -eq 1 ]]; then");
        builder.AppendLine($"    COMPREPLY=( $(compgen -W \"{commandList}\" -- \"$cur\") )");
        builder.AppendLine("    return 0");
        builder.AppendLine("  fi");
        builder.AppendLine();
        builder.AppendLine("  if [[ \"$command\" == \"init\" && \"$prev\" == \"--template\" ]]; then");
        builder.AppendLine($"    COMPREPLY=( $(compgen -W \"{templateList}\" -- \"$cur\") )");
        builder.AppendLine("    return 0");
        builder.AppendLine("  fi");
        builder.AppendLine();
        builder.AppendLine("  case \"$command\" in");
        foreach (var (command, options) in CommandOptions)
        {
            builder.AppendLine($"    {command})");
            builder.AppendLine($"      COMPREPLY=( $(compgen -W \"{string.Join(" ", options)}\" -- \"$cur\") )");
            builder.AppendLine("      ;;");
        }

        builder.AppendLine("  esac");
        builder.AppendLine("}");
        builder.AppendLine("complete -F _sprmq_completion sprmq");
        return builder.ToString().TrimEnd();
    }

    private static string RenderZsh(IReadOnlyList<string> templateNames)
    {
        var bashScript = RenderBash(templateNames);
        return $$"""
        autoload -Uz bashcompinit
        bashcompinit

        {{bashScript}}
        """.TrimEnd();
    }

    private static string RenderPwsh(IReadOnlyList<string> templateNames)
    {
        var commandList = string.Join("', '", CommandOptions.Keys);
        var templateList = string.Join("', '", templateNames);
        var optionsMap = string.Join(
            $"{Environment.NewLine}    ",
            CommandOptions.Select(entry => $"'{entry.Key}' = @('{string.Join("', '", entry.Value)}')"));

        return $$"""
        Register-ArgumentCompleter -Native -CommandName sprmq -ScriptBlock {
            param($wordToComplete, $commandAst, $cursorPosition)

            $commands = @('{{commandList}}')
            $templateNames = @('{{templateList}}')
            $optionsByCommand = @{
                {{optionsMap}}
            }

            $elements = @($commandAst.CommandElements | ForEach-Object { $_.Value })

            if ($elements.Count -le 1) {
                $commands |
                    Where-Object { $_ -like "$wordToComplete*" } |
                    ForEach-Object { [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_) }
                return
            }

            $command = $elements[1]
            $previous = if ($elements.Count -ge 3) { $elements[-2] } else { '' }

            if ($command -eq 'init' -and $previous -eq '--template') {
                $templateNames |
                    Where-Object { $_ -like "$wordToComplete*" } |
                    ForEach-Object { [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_) }
                return
            }

            $options = if ($optionsByCommand.ContainsKey($command)) { $optionsByCommand[$command] } else { @() }
            $options |
                Where-Object { $_ -like "$wordToComplete*" } |
                ForEach-Object { [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterName', $_) }
        }
        """.TrimEnd();
    }
}
