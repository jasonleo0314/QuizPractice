using Microsoft.Extensions.Configuration;
using QuizPractice;

namespace QuizPractice.Web;

public sealed class QuizWebSession
{
    private readonly object _sync = new();
    private readonly QuizOptions _options;
    private readonly IReadOnlyList<Question> _questions;
    private readonly ProgressService _progressService;
    private readonly ExcelReportService _reportService = new();
    private readonly Random _random;
    private Question? _currentQuestion;
    private QuestionFeedback? _feedback;
    private bool _exited;

    public QuizWebSession(IConfiguration configuration)
    {
        _options = configuration.GetSection("Quiz").Get<QuizOptions>() ?? new QuizOptions();
        _questions = QuestionBankLoader.Load(ResolvePath(_options.QuestionBankPath));
        _progressService = new ProgressService(
            ResolvePath(_options.ProgressPath),
            _questions,
            _options.RequiredConsecutiveCorrect,
            _options.QuestionBankPath);
        _random = _options.RandomSeed is null ? new Random() : new Random(_options.RandomSeed.Value);
    }

    public QuizStateResponse GetState()
    {
        lock (_sync)
        {
            if (!_exited && _feedback is null)
            {
                EnsureCurrentQuestion();
            }

            return CreateState();
        }
    }

    public QuizStateResponse SubmitAnswer(string? answer)
    {
        lock (_sync)
        {
            var trimmedAnswer = answer?.Trim() ?? string.Empty;
            if (_options.ExitKeys.Contains(trimmedAnswer, StringComparer.OrdinalIgnoreCase))
            {
                return ExitCore();
            }

            _exited = false;
            if (_feedback is not null)
            {
                return CreateState();
            }

            EnsureCurrentQuestion();
            if (_currentQuestion is null)
            {
                SaveAll();
                return CreateState(_options.Texts.Finished);
            }

            if (!AnswerNormalizer.IsValid(trimmedAnswer, _currentQuestion))
            {
                return CreateState(_options.Texts.InvalidAnswer);
            }

            var normalizedAnswer = AnswerNormalizer.Normalize(trimmedAnswer, _currentQuestion.IsMultiple);
            var isCorrect = normalizedAnswer == _currentQuestion.CorrectAnswer;
            _progressService.RecordAnswer(_currentQuestion, normalizedAnswer, isCorrect);
            SaveAll();
            _feedback = QuestionFeedback.Create(_currentQuestion, normalizedAnswer, isCorrect, _progressService[_currentQuestion]);
            _currentQuestion = null;

            return CreateState(_options.Texts.Saved);
        }
    }

    public QuizStateResponse NextQuestion()
    {
        lock (_sync)
        {
            _exited = false;
            _feedback = null;
            EnsureCurrentQuestion();
            return CreateState();
        }
    }

    public QuizStateResponse Exit()
    {
        lock (_sync)
        {
            return ExitCore();
        }
    }

    public QuizReport GetReport()
    {
        lock (_sync)
        {
            SaveAll();
            var path = ResolvePath(_options.ExcelReportPath);
            return new QuizReport(File.ReadAllBytes(path), Path.GetFileName(path));
        }
    }

    private QuizStateResponse ExitCore()
    {
        _exited = true;
        _feedback = null;
        _currentQuestion = null;
        SaveAll();
        return CreateState(_options.Texts.Goodbye);
    }

    private void EnsureCurrentQuestion()
    {
        if (_currentQuestion is not null)
        {
            return;
        }

        _currentQuestion = _progressService.PickNextQuestion(_random);
        if (_currentQuestion is null)
        {
            SaveAll();
        }
    }

    private QuizStateResponse CreateState(string? message = null)
    {
        var statistics = _progressService.GetStatistics();
        var activeQuestion = _feedback?.Question ?? _currentQuestion;
        var activeProgress = activeQuestion is null ? null : _progressService[activeQuestion];
        return new QuizStateResponse(
            _options.Texts,
            _options.Texts.Prompt,
            string.Join('/', _options.ExitKeys),
            BuildArchiveRuleText(),
            _options.RequiredConsecutiveCorrect,
            _questions.Count,
            Path.GetFileName(_options.ProgressPath),
            Path.GetFileName(_options.ExcelReportPath),
            StatisticsDto.From(statistics),
            activeQuestion is null ? null : QuestionDto.From(activeQuestion),
            activeProgress is null ? null : ProgressDto.From(activeProgress),
            _feedback is null ? null : FeedbackDto.From(_feedback),
            statistics.Remaining == 0,
            _exited,
            message);
    }

    private void SaveAll()
    {
        _progressService.Save();
        _reportService.Export(
            ResolvePath(_options.ExcelReportPath),
            _questions,
            _progressService.Store,
            _options.RequiredConsecutiveCorrect);
    }

    private static string ResolvePath(string path)
    {
        return Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
    }

    private static string BuildArchiveRuleText()
    {
        return "答对次数大于答错次数";
    }
}

public sealed record AnswerRequest(string? Answer);

public sealed record QuizReport(byte[] Content, string FileName);

public sealed record QuizStateResponse(
    QuizTexts Texts,
    string Prompt,
    string ExitKeysText,
    string ArchiveRule,
    int RequiredConsecutiveCorrect,
    int LoadedQuestionCount,
    string ProgressFileName,
    string ExcelReportFileName,
    StatisticsDto Statistics,
    QuestionDto? Question,
    ProgressDto? Progress,
    FeedbackDto? Feedback,
    bool Completed,
    bool Exited,
    string? Message);

public sealed record StatisticsDto(
    int Total,
    int Completed,
    int Remaining,
    int Attempts,
    int Correct,
    int Wrong,
    double CompletionRate,
    double AccuracyRate)
{
    public static StatisticsDto From(QuizStatistics statistics)
    {
        return new StatisticsDto(
            statistics.Total,
            statistics.Completed,
            statistics.Remaining,
            statistics.Attempts,
            statistics.Correct,
            statistics.Wrong,
            statistics.CompletionRate,
            statistics.AccuracyRate);
    }
}

public sealed record QuestionDto(
    string Id,
    string Type,
    string Number,
    string Text,
    IReadOnlyDictionary<string, string> Options,
    string CorrectAnswer,
    bool IsJudge,
    bool IsMultiple)
{
    public static QuestionDto From(Question question)
    {
        return new QuestionDto(
            question.Id,
            question.Type,
            question.Number,
            question.Text,
            question.Options,
            question.CorrectAnswer,
            question.IsJudge,
            question.IsMultiple);
    }
}

public sealed record ProgressDto(
    int Attempts,
    int CorrectCount,
    int WrongCount,
    int ConsecutiveCorrect,
    string? LastAnswer,
    bool Completed,
    DateTimeOffset? LastAnsweredAt)
{
    public static ProgressDto From(QuestionProgress progress)
    {
        return new ProgressDto(
            progress.Attempts,
            progress.CorrectCount,
            progress.WrongCount,
            progress.ConsecutiveCorrect,
            progress.LastAnswer,
            progress.Completed,
            progress.LastAnsweredAt);
    }
}

public sealed record FeedbackDto(
    string Answer,
    bool IsCorrect,
    int CorrectCount,
    int WrongCount,
    int ConsecutiveCorrect,
    bool Completed)
{
    public static FeedbackDto From(QuestionFeedback feedback)
    {
        return new FeedbackDto(
            feedback.Answer,
            feedback.IsCorrect,
            feedback.CorrectCount,
            feedback.WrongCount,
            feedback.ConsecutiveCorrect,
            feedback.Completed);
    }
}
