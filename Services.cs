using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using ClosedXML.Excel;

namespace QuizPractice;

public static class QuestionBankLoader
{
    public static IReadOnlyList<Question> Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"题库文件不存在：{path}", path);
        }

        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var header = reader.ReadLine();
        if (string.IsNullOrWhiteSpace(header))
        {
            return [];
        }

        var questions = new List<Question>();
        while (!reader.EndOfStream)
        {
            var line = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var columns = line.Split('\t');
            if (columns.Length < 6)
            {
                continue;
            }

            var optionKeys = new[] { "A", "B", "C", "D" };
            var optionValues = columns
                .Skip(3)
                .Take(Math.Min(optionKeys.Length, columns.Length - 4))
                .Select(option => option.Trim())
                .ToList();

            var options = optionKeys
                .Take(optionValues.Count)
                .Zip(optionValues, (key, value) => new KeyValuePair<string, string>(key, value))
                .ToDictionary(option => option.Key, option => option.Value);

            foreach (var emptyOption in options.Where(option => string.IsNullOrWhiteSpace(option.Value)).Select(option => option.Key).ToList())
            {
                options.Remove(emptyOption);
            }

            questions.Add(new Question(
                columns[0].Trim(),
                columns[1].Trim(),
                columns[2].Trim(),
                options,
                AnswerNormalizer.Normalize(columns[^1], isMultiple: columns[0].Contains("多选", StringComparison.OrdinalIgnoreCase))));
        }

        return questions;
    }
}

public static class AnswerNormalizer
{
    public static string Normalize(string answer, bool isMultiple)
    {
        var normalized = new string(answer
            .Where(char.IsLetter)
            .Select(char.ToUpperInvariant)
            .Distinct()
            .Order()
            .ToArray());

        return isMultiple ? normalized : normalized[..Math.Min(normalized.Length, 1)];
    }

    public static bool IsValid(string answer, Question question)
    {
        var normalized = Normalize(answer, question.IsMultiple);
        if (string.IsNullOrEmpty(normalized))
        {
            return false;
        }

        return normalized.All(letter => question.Options.ContainsKey(letter.ToString()))
            && (question.IsMultiple || normalized.Length == 1);
    }
}

public sealed class ProgressService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly string _path;
    private readonly IReadOnlyList<Question> _questions;
    private readonly int _requiredConsecutiveCorrect;

    public ProgressStore Store { get; }

    public ProgressService(string path, IReadOnlyList<Question> questions, int requiredConsecutiveCorrect, string questionBankPath)
    {
        _path = path;
        _questions = questions;
        _requiredConsecutiveCorrect = requiredConsecutiveCorrect;
        Store = Load(path) ?? new ProgressStore { QuestionBankPath = questionBankPath };
        EnsureQuestionProgress();
    }

    public QuestionProgress this[Question question] => Store.Questions[question.Id];

    public IReadOnlyList<Question> RemainingQuestions => _questions
        .Where(question => !Store.Questions[question.Id].Completed)
        .ToList();

    public Question? PickNextQuestion(Random random)
    {
        ArgumentNullException.ThrowIfNull(random);

        var unansweredQuestions = _questions
            .Where(question =>
            {
                var progress = Store.Questions[question.Id];
                return !progress.Completed && progress.Attempts == 0;
            })
            .ToList();

        if (unansweredQuestions.Count > 0)
        {
            return unansweredQuestions[random.Next(unansweredQuestions.Count)];
        }

        var remainingQuestions = RemainingQuestions;
        return remainingQuestions.Count == 0
            ? null
            : remainingQuestions[random.Next(remainingQuestions.Count)];
    }

    public void RecordAnswer(Question question, string answer, bool isCorrect)
    {
        var progress = Store.Questions[question.Id];
        progress.Attempts++;
        progress.LastAnswer = answer;
        progress.LastAnsweredAt = DateTimeOffset.Now;

        if (isCorrect)
        {
            progress.CorrectCount++;
            progress.ConsecutiveCorrect++;
        }
        else
        {
            progress.WrongCount++;
            progress.ConsecutiveCorrect = 0;
        }

        progress.Completed = IsCompleted(progress);

        Save();
    }

    public QuizStatistics GetStatistics()
    {
        var values = Store.Questions.Values;
        return new QuizStatistics(
            _questions.Count,
            values.Count(progress => progress.Completed),
            values.Count(progress => !progress.Completed),
            values.Sum(progress => progress.Attempts),
            values.Sum(progress => progress.CorrectCount),
            values.Sum(progress => progress.WrongCount));
    }

    public void Save()
    {
        Store.UpdatedAt = DateTimeOffset.Now;
        File.WriteAllText(_path, JsonSerializer.Serialize(Store, JsonOptions), Encoding.UTF8);
    }

    private static ProgressStore? Load(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var json = File.ReadAllText(path, Encoding.UTF8);
        return JsonSerializer.Deserialize<ProgressStore>(json, JsonOptions);
    }

    private void EnsureQuestionProgress()
    {
        foreach (var question in _questions)
        {
            if (!Store.Questions.ContainsKey(question.Id))
            {
                Store.Questions[question.Id] = new QuestionProgress { QuestionId = question.Id };
            }
        }

        foreach (var staleId in Store.Questions.Keys.Except(_questions.Select(question => question.Id)).ToList())
        {
            Store.Questions.Remove(staleId);
        }

        foreach (var progress in Store.Questions.Values)
        {
            progress.Completed = IsCompleted(progress);
        }

        Save();
    }

    private bool IsCompleted(QuestionProgress progress)
    {
        return progress.CorrectCount > progress.WrongCount;
    }
}

public sealed class ExcelReportService
{
    public void Export(string path, IReadOnlyList<Question> questions, ProgressStore store, int requiredConsecutiveCorrect)
    {
        using var workbook = new XLWorkbook();
        CreateSummarySheet(workbook, questions, store, requiredConsecutiveCorrect);
        CreateDetailsSheet(workbook, questions, store);
        CreateGroupedSheet(workbook, questions, store);
        workbook.SaveAs(path);
    }

    private static void CreateSummarySheet(XLWorkbook workbook, IReadOnlyList<Question> questions, ProgressStore store, int requiredConsecutiveCorrect)
    {
        var sheet = workbook.Worksheets.Add("统计概览");
        var statistics = new QuizStatistics(
            questions.Count,
            store.Questions.Values.Count(progress => progress.Completed),
            store.Questions.Values.Count(progress => !progress.Completed),
            store.Questions.Values.Sum(progress => progress.Attempts),
            store.Questions.Values.Sum(progress => progress.CorrectCount),
            store.Questions.Values.Sum(progress => progress.WrongCount));
        var rows = new (string Name, object Value)[]
        {
            ("题目总数", questions.Count),
            ("已归档（答对次数大于答错次数）", statistics.Completed),
            ("待练习", statistics.Remaining),
            ("总作答次数", statistics.Attempts),
            ("答对次数", statistics.Correct),
            ("答错次数", statistics.Wrong),
            ("归档率", $"{statistics.CompletionRate:F2}%"),
            ("正确率", $"{statistics.AccuracyRate:F2}%"),
            ("更新时间", store.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss"))
        };

        sheet.Cell(1, 1).Value = "项目";
        sheet.Cell(1, 2).Value = "数值";
        for (var i = 0; i < rows.Length; i++)
        {
            sheet.Cell(i + 2, 1).Value = rows[i].Name;
            sheet.Cell(i + 2, 2).Value = XLCellValue.FromObject(rows[i].Value);
        }

        FormatHeader(sheet.Range(1, 1, 1, 2));
        sheet.Columns().AdjustToContents();
    }

    private static void CreateDetailsSheet(XLWorkbook workbook, IReadOnlyList<Question> questions, ProgressStore store)
    {
        var sheet = workbook.Worksheets.Add("题目明细");
        var headers = new[] { "题型", "编号", "题干", "正确答案", "作答次数", "正确次数", "错误次数", "连续正确", "是否完成", "上次答案", "上次作答时间" };
        WriteHeaders(sheet, headers);

        for (var i = 0; i < questions.Count; i++)
        {
            var question = questions[i];
            var progress = store.Questions[question.Id];
            var row = i + 2;
            sheet.Cell(row, 1).Value = question.Type;
            sheet.Cell(row, 2).Value = question.Number;
            sheet.Cell(row, 3).Value = question.Text;
            sheet.Cell(row, 4).Value = question.CorrectAnswer;
            sheet.Cell(row, 5).Value = progress.Attempts;
            sheet.Cell(row, 6).Value = progress.CorrectCount;
            sheet.Cell(row, 7).Value = progress.WrongCount;
            sheet.Cell(row, 8).Value = progress.ConsecutiveCorrect;
            sheet.Cell(row, 9).Value = progress.Completed ? "是" : "否";
            sheet.Cell(row, 10).Value = progress.LastAnswer ?? string.Empty;
            sheet.Cell(row, 11).Value = progress.LastAnsweredAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty;
        }

        sheet.SheetView.FreezeRows(1);
        sheet.Columns().AdjustToContents();
    }

    private static void CreateGroupedSheet(XLWorkbook workbook, IReadOnlyList<Question> questions, ProgressStore store)
    {
        var sheet = workbook.Worksheets.Add("按正确错误次数划分");
        var headers = new[] { "分组", "题型", "编号", "题干", "正确答案", "正确次数", "错误次数", "连续正确" };
        WriteHeaders(sheet, headers);

        var row = 2;
        foreach (var item in questions
            .Select(question => new { Question = question, Progress = store.Questions[question.Id] })
            .OrderBy(item => item.Progress.Completed)
            .ThenByDescending(item => item.Progress.WrongCount)
            .ThenBy(item => item.Progress.CorrectCount))
        {
            var group = item.Progress.Completed
                ? "已归档"
                : item.Progress.WrongCount > 0
                    ? $"未完成-错{item.Progress.WrongCount}次"
                    : $"未完成-对{item.Progress.CorrectCount}次";

            sheet.Cell(row, 1).Value = group;
            sheet.Cell(row, 2).Value = item.Question.Type;
            sheet.Cell(row, 3).Value = item.Question.Number;
            sheet.Cell(row, 4).Value = item.Question.Text;
            sheet.Cell(row, 5).Value = item.Question.CorrectAnswer;
            sheet.Cell(row, 6).Value = item.Progress.CorrectCount;
            sheet.Cell(row, 7).Value = item.Progress.WrongCount;
            sheet.Cell(row, 8).Value = item.Progress.ConsecutiveCorrect;
            row++;
        }

        sheet.SheetView.FreezeRows(1);
        sheet.Columns().AdjustToContents();
    }

    private static void WriteHeaders(IXLWorksheet sheet, IReadOnlyList<string> headers)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            sheet.Cell(1, i + 1).Value = headers[i];
        }

        FormatHeader(sheet.Range(1, 1, 1, headers.Count));
    }

    private static void FormatHeader(IXLRange range)
    {
        range.Style.Font.Bold = true;
        range.Style.Fill.BackgroundColor = XLColor.FromHtml("#1F4E78");
        range.Style.Font.FontColor = XLColor.White;
    }
}
