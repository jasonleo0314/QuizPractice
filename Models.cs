using System.Text.Json.Serialization;

namespace QuizPractice;

public sealed class QuizOptions
{
    public string QuestionBankPath { get; set; } = "QuizPractice.tsv";
    public string ProgressPath { get; set; } = "quiz-progress.json";
    public string ExcelReportPath { get; set; } = "作答情况.xlsx";
    public int RequiredConsecutiveCorrect { get; set; } = 2;
    public int StatisticsInterval { get; set; } = 10;
    public int ClearScreenInterval { get; set; } = 2;
    public int? RandomSeed { get; set; }
    public string[] ExitKeys { get; set; } = ["q", "Q"];
    public QuizTexts Texts { get; set; } = new();
}

public sealed class QuizTexts
{
    public string Title { get; set; } = "题库练习";
    public string Subtitle { get; set; } = "随机抽题 · 自动保存";
    public string Prompt { get; set; } = "请输入答案：";
    public string Correct { get; set; } = "回答正确";
    public string Wrong { get; set; } = "回答错误";
    public string CorrectAnswer { get; set; } = "正确答案";
    public string Finished { get; set; } = "已完成全部题目。";
    public string Saved { get; set; } = "进度已保存";
    public string InvalidAnswer { get; set; } = "答案格式无效，请重新输入。";
    public string Goodbye { get; set; } = "已保存进度，欢迎下次继续。";
}

public sealed record Question(
    string Type,
    string Number,
    string Text,
    IReadOnlyDictionary<string, string> Options,
    string CorrectAnswer)
{
    public string Id => $"{Type}-{Number}";
    public bool IsJudge => Type.Contains("judgment", StringComparison.OrdinalIgnoreCase);
    public bool IsMultiple => Type.Contains("multiple_choice", StringComparison.OrdinalIgnoreCase);
    public string TypeDisplayName => Type switch
    {
        "single_choice" => "单选题",
        "multiple_choice" => "多选题",
        "judgment" => "判断题",
        _ => Type
    };
}

public sealed class QuestionProgress
{
    public string QuestionId { get; set; } = string.Empty;
    public int Attempts { get; set; }
    public int CorrectCount { get; set; }
    public int WrongCount { get; set; }
    public int ConsecutiveCorrect { get; set; }
    public string? LastAnswer { get; set; }
    public bool Completed { get; set; }
    public DateTimeOffset? LastAnsweredAt { get; set; }
}

public sealed class ProgressStore
{
    public string QuestionBankPath { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    public Dictionary<string, QuestionProgress> Questions { get; set; } = [];
}

public sealed record QuizStatistics(
    int Total,
    int Completed,
    int Remaining,
    int Attempts,
    int Correct,
    int Wrong)
{
    [JsonIgnore]
    public double CompletionRate => Total == 0 ? 0 : Completed * 100.0 / Total;

    [JsonIgnore]
    public double AccuracyRate => Attempts == 0 ? 0 : Correct * 100.0 / Attempts;
}

public sealed record QuestionFeedback(
    Question Question,
    string Answer,
    bool IsCorrect,
    int CorrectCount,
    int WrongCount,
    int ConsecutiveCorrect,
    bool Completed)
{
    public static QuestionFeedback Create(Question question, string answer, bool isCorrect, QuestionProgress progress)
    {
        return new QuestionFeedback(
            question,
            answer,
            isCorrect,
            progress.CorrectCount,
            progress.WrongCount,
            progress.ConsecutiveCorrect,
            progress.Completed);
    }
}
