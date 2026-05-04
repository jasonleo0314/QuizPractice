using Microsoft.Extensions.Configuration;
using Spectre.Console;
using QuizPractice;

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.InputEncoding = System.Text.Encoding.UTF8;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .Build();

var options = configuration.GetSection("Quiz").Get<QuizOptions>() ?? new QuizOptions();
var random = options.RandomSeed is null ? new Random() : new Random(options.RandomSeed.Value);
var reportService = new ExcelReportService();
ProgressService? progressService = null;
IReadOnlyList<Question> questions = [];

try
{
    questions = QuestionBankLoader.Load(ResolvePath(options.QuestionBankPath));
    progressService = new ProgressService(
        ResolvePath(options.ProgressPath),
        questions,
        options.RequiredConsecutiveCorrect,
        options.QuestionBankPath);

    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        SaveAll(progressService, reportService, questions, options);
        AnsiConsole.MarkupLine($"[green]{Markup.Escape(options.Texts.Goodbye)}[/]");
        Environment.Exit(0);
    };

    QuizConsoleRenderer.RenderWelcome(options, questions, progressService);

    while (true)
    {
        var remaining = progressService.RemainingQuestions;
        if (remaining.Count == 0)
        {
            SaveAll(progressService, reportService, questions, options);
            QuizConsoleRenderer.RenderCompletionMessage(options, progressService.GetStatistics());
            break;
        }

        var question = remaining[random.Next(remaining.Count)];
        QuizConsoleRenderer.RenderScreen(question, progressService, options);

        var answer = QuizConsoleRenderer.ReadAnswer(question, options);
        if (answer is null)
        {
            SaveAll(progressService, reportService, questions, options);
            AnsiConsole.MarkupLine($"[green]{Markup.Escape(options.Texts.Goodbye)}[/]");
            break;
        }

        var normalizedAnswer = AnswerNormalizer.Normalize(answer, question.IsMultiple);
        var isCorrect = normalizedAnswer == question.CorrectAnswer;
        progressService.RecordAnswer(question, normalizedAnswer, isCorrect);
        SaveAll(progressService, reportService, questions, options);
        QuizConsoleRenderer.RenderAnswerFeedback(
            QuestionFeedback.Create(question, normalizedAnswer, isCorrect, progressService[question]),
            options);
    }
}
catch (Exception ex)
{
    AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything);
    if (progressService is not null)
    {
        SaveAll(progressService, reportService, questions, options);
    }
}

static string ResolvePath(string path)
{
    return Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
}

static void SaveAll(ProgressService progressService, ExcelReportService reportService, IReadOnlyList<Question> questions, QuizOptions options)
{
    progressService.Save();
    reportService.Export(
        ResolvePath(options.ExcelReportPath),
        questions,
        progressService.Store,
        options.RequiredConsecutiveCorrect);
}
