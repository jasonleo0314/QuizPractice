using Spectre.Console;

namespace QuizPractice;

public static class QuizConsoleRenderer
{
    private static readonly Style HeaderBorderStyle = new(Color.Cyan1);
    private static readonly Style PrimaryBorderStyle = new(Color.Blue);
    private static readonly Style MutedBorderStyle = new(Color.Grey37);

    public static void RenderWelcome(QuizOptions options, IReadOnlyList<Question> questions, ProgressService progressService)
    {
        AnsiConsole.Clear();
        RenderHeader(options);
        AnsiConsole.WriteLine();
        RenderStatistics(progressService.GetStatistics());

        var summaryGrid = new Grid();
        summaryGrid.AddColumn();
        summaryGrid.AddColumn();
        summaryGrid.AddRow(
            $"[grey]已加载题目[/]\n[bold white]{questions.Count}[/]",
            $"[grey]归档条件[/]\n[white]{Markup.Escape(BuildArchiveRuleText())}[/]");
        summaryGrid.AddRow(
            "[grey]统计频率[/]\n[white]每题展示实时统计[/]",
            $"[grey]快捷操作[/]\n[yellow]{Markup.Escape(string.Join('/', options.ExitKeys))}[/] [grey]随时退出[/]");

        AnsiConsole.Write(CreatePanel(summaryGrid, "[bold deepskyblue1]练习说明[/]", PrimaryBorderStyle, new Padding(1, 1)));
        AnsiConsole.WriteLine();
    }

    public static void RenderScreen(Question question, ProgressService progressService, QuizOptions options)
    {
        AnsiConsole.Clear();
        RenderHeader(options);
        AnsiConsole.WriteLine();
        RenderStatistics(progressService.GetStatistics());
        AnsiConsole.WriteLine();
        RenderQuestion(question, progressService, options);
    }

    public static string? ReadAnswer(Question question, QuizOptions options)
    {
        while (true)
        {
            var answer = AnsiConsole.Ask<string>($"[bold cyan]{Markup.Escape(options.Texts.Prompt)}[/]").Trim();
            if (options.ExitKeys.Contains(answer, StringComparer.OrdinalIgnoreCase))
            {
                return null;
            }

            if (AnswerNormalizer.IsValid(answer, question))
            {
                return answer;
            }

            AnsiConsole.MarkupLine($"[red]{Markup.Escape(options.Texts.InvalidAnswer)}[/]");
        }
    }

    public static void RenderAnswerFeedback(QuestionFeedback feedback, QuizOptions options)
    {
        AnsiConsole.Clear();
        RenderHeader(options);
        AnsiConsole.WriteLine();

        var status = feedback.IsCorrect
            ? $"[bold green]✓ {Markup.Escape(options.Texts.Correct)}[/]"
            : $"[bold red]✗ {Markup.Escape(options.Texts.Wrong)}[/]";
        var archiveStatus = feedback.Completed ? "[green]已归档[/]" : "[yellow]继续巩固[/]";

        AnsiConsole.Write(CreatePanel(
            $"{status}  [grey]· {Markup.Escape(feedback.Question.TypeDisplayName)} 第 {Markup.Escape(feedback.Question.Number)} 题[/]\n" +
            $"[grey]你的答案：[/] [white]{Markup.Escape(FormatAnswerDisplay(feedback.Question, feedback.Answer))}[/]    " +
            $"[grey]{Markup.Escape(options.Texts.CorrectAnswer)}：[/] [bold yellow]{Markup.Escape(FormatAnswerDisplay(feedback.Question, feedback.Question.CorrectAnswer))}[/]\n" +
            $"[grey]本题状态：[/] {archiveStatus}  [grey]· 对/错[/] [green]{feedback.CorrectCount}[/]/[red]{feedback.WrongCount}[/]\n\n" +
            BuildQuestionContent(feedback.Question, feedback.Question.CorrectAnswer, feedback.Answer),
            "[bold deepskyblue1]作答反馈[/]",
            new Style(feedback.IsCorrect ? Color.Green : Color.Red),
            new Padding(1, 1)));

        AnsiConsole.Write(CreatePanel(
            $"[green]{Markup.Escape(options.Texts.Saved)}[/] [grey]({Markup.Escape(options.ProgressPath)} / {Markup.Escape(options.ExcelReportPath)})[/]\n[grey]按任意键继续下一题...[/]",
            null,
            MutedBorderStyle,
            new Padding(1, 0, 1, 0)));

        Console.ReadKey(intercept: true);
    }

    public static void RenderCompletionMessage(QuizOptions options, QuizStatistics statistics)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(CreatePanel(
            $"[bold green]{Markup.Escape(options.Texts.Finished)}[/]\n" +
            $"[grey]已归档[/] [green]{statistics.Completed}[/] [grey]/[/] [white]{statistics.Total}[/]  [grey]· 正确率[/] [white]{FormatPercent(statistics.AccuracyRate)}[/]",
            null,
            new Style(Color.Green),
            new Padding(1, 1)));
    }

    private static void RenderHeader(QuizOptions options)
    {
        var headerGrid = new Grid();
        headerGrid.AddColumn();
        headerGrid.AddColumn(new GridColumn().RightAligned());
        headerGrid.AddRow(
            $"[bold cyan]{Markup.Escape(options.Texts.Title)}[/]\n[grey]{Markup.Escape(options.Texts.Subtitle)}[/]",
            $"[grey]{DateTime.Now:yyyy-MM-dd HH:mm}[/]");

        AnsiConsole.Write(CreatePanel(headerGrid, null, HeaderBorderStyle, new Padding(1, 0, 1, 0)));
    }

    private static void RenderQuestion(Question question, ProgressService progressService, QuizOptions options)
    {
        var progress = progressService[question];
        AnsiConsole.Write(CreatePanel(
            BuildQuestionContent(question),
            $"[bold deepskyblue1]{Markup.Escape(question.TypeDisplayName)} · 第 {Markup.Escape(question.Number)} 题[/]",
            PrimaryBorderStyle,
            new Padding(1, 1)));

        var mastery = progress.Completed ? "[green]已归档[/]" : "[yellow]继续巩固[/]";
        AnsiConsole.Write(CreatePanel(
            $"[grey]本题进度[/]  作答 [white]{progress.Attempts}[/]  ·  正确 [green]{progress.CorrectCount}[/]  ·  错误 [red]{progress.WrongCount}[/]\n" +
            $"[grey]归档状态[/]  {mastery}  [grey]· 条件[/] [white]{Markup.Escape(BuildArchiveRuleText())}[/]",
            null,
            progress.Completed ? new Style(Color.Green) : MutedBorderStyle,
            new Padding(1, 0, 1, 0)));
    }

    private static void RenderStatistics(QuizStatistics statistics)
    {
        var table = new Table()
            .RoundedBorder()
            .BorderColor(Color.Grey37)
            .Expand()
            .AddColumn("总题数")
            .AddColumn("已归档")
            .AddColumn("待练习")
            .AddColumn("作答")
            .AddColumn("正确")
            .AddColumn("错误")
            .AddColumn("归档率")
            .AddColumn("正确率");

        table.AddRow(
            statistics.Total.ToString(),
            $"[green]{statistics.Completed}[/]",
            $"[yellow]{statistics.Remaining}[/]",
            $"[white]{statistics.Attempts}[/]",
            $"[green]{statistics.Correct}[/]",
            $"[red]{statistics.Wrong}[/]",
            $"[cyan1]{FormatPercent(statistics.CompletionRate)}[/]",
            $"[cyan1]{FormatPercent(statistics.AccuracyRate)}[/]");

        AnsiConsole.Write(table);
    }

    private static string BuildQuestionContent(Question question, string? highlightCorrectAnswer = null, string? userAnswer = null)
    {
        var lines = new List<string> { $"[white]{Markup.Escape(question.Text)}[/]" };
        if (question.Options.Count > 0)
        {
            lines.Add(string.Empty);
        }

        var correctSet = ParseAnswerSet(highlightCorrectAnswer);
        var userSet = ParseAnswerSet(userAnswer);

        foreach (var option in question.Options)
        {
            var key = option.Key.ToUpperInvariant();
            var prefixStyle = "deepskyblue1";
            var textStyle = "white";
            var markers = new List<string>();

            if (correctSet.Contains(key))
            {
                prefixStyle = "green1";
                textStyle = "green1";
                markers.Add("[green1]✓正确[/]");

                if (question.IsMultiple && !userSet.Contains(key))
                {
                    prefixStyle = "yellow1";
                    textStyle = "yellow1";
                    markers.Add("[yellow1]!漏选[/]");
                }
            }

            if (userSet.Contains(key) && !correctSet.Contains(key))
            {
                prefixStyle = "red1";
                textStyle = "red1";
                markers.Add("[red1]✗你的选择[/]");
            }
            else if (userSet.Contains(key) && correctSet.Contains(key))
            {
                markers.Add("[green1]你的选择[/]");
            }

            var markerText = markers.Count > 0 ? $" [grey]([/]{string.Join("[grey], [/]", markers)}[grey])[/]" : string.Empty;
            lines.Add($"[{prefixStyle}]{key}.[/] [{textStyle}]{Markup.Escape(option.Value)}[/]{markerText}");
        }

        return string.Join("\n", lines);
    }

    private static string FormatAnswerDisplay(Question question, string answer)
    {
        var answerSet = ParseAnswerSet(answer).ToList();
        if (answerSet.Count == 0)
        {
            return answer;
        }

        if (question.IsJudge)
        {
            return string.Join('、', answerSet.Select(option => question.Options.TryGetValue(option, out var text) ? text : option));
        }

        return string.Join('、', answerSet.Select(option => question.Options.TryGetValue(option, out var text) ? $"{option}({text})" : option));
    }

    private static HashSet<string> ParseAnswerSet(string? answer)
    {
        if (string.IsNullOrWhiteSpace(answer))
        {
            return [];
        }

        return answer
            .Where(char.IsLetter)
            .Select(char.ToUpperInvariant)
            .Select(letter => letter.ToString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static Panel CreatePanel(Grid content, string? header, Style borderStyle, Padding padding)
    {
        var panel = new Panel(content)
        {
            Border = BoxBorder.Rounded,
            BorderStyle = borderStyle,
            Padding = padding
        };

        if (!string.IsNullOrWhiteSpace(header))
        {
            panel.Header = new PanelHeader(header);
        }

        return panel;
    }

    private static Panel CreatePanel(string content, string? header, Style borderStyle, Padding padding)
    {
        var panel = new Panel(content)
        {
            Border = BoxBorder.Rounded,
            BorderStyle = borderStyle,
            Padding = padding
        };

        if (!string.IsNullOrWhiteSpace(header))
        {
            panel.Header = new PanelHeader(header);
        }

        return panel;
    }

    private static string BuildArchiveRuleText()
    {
        return "答对次数大于答错次数";
    }

    private static string FormatPercent(double value)
    {
        return $"{value:F2}%";
    }
}
