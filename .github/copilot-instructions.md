# Copilot instructions for QuizPractice

## Build, test, and run

- Restore dependencies: `dotnet restore`
- Build: `dotnet build`
- Run the app: `dotnet run --project QuizPractice.csproj`
- There is currently no test project or lint/format configuration in this repository. If tests are added later, document both the full-suite command and the single-test command here.

The project targets .NET 8 and uses a `.slnx` solution containing `QuizPractice.csproj`.

## High-level architecture

QuizPractice is a .NET console quiz-practice app for repeated question drilling, progress persistence, and Excel reporting.

- `Program.cs` is the composition root. It loads `appsettings.json` through `Microsoft.Extensions.Configuration`, resolves configured file paths relative to `AppContext.BaseDirectory`, creates services, handles Ctrl+C, runs the main random-question loop, records answers, and calls the shared save/export path after each answer and on exit.
- `Models.cs` contains the app configuration model (`QuizOptions`/`QuizTexts`), quiz domain records (`Question`, `QuestionFeedback`, `QuizStatistics`), and persisted progress models (`QuestionProgress`, `ProgressStore`).
- `Services.cs` contains non-UI behavior:
  - `QuestionBankLoader` reads UTF-8 TSV quiz banks, skips blank/short rows, trims fields, removes empty options, and normalizes correct answers.
  - `AnswerNormalizer` keeps only letters, uppercases them, de-duplicates, sorts multi-select answers, and truncates single-select answers to one letter.
  - `ProgressService` owns progress state, initializes missing question progress, removes stale question IDs, marks completion, and writes JSON progress.
  - `ExcelReportService` writes `作答情况.xlsx` with summary, detail, and grouped-by-progress sheets using ClosedXML.
- `QuizConsoleRenderer.cs` is the Spectre.Console UI layer. Keep rendering and input prompting there rather than in services.

The normal runtime data flow is: load TSV questions -> load or create JSON progress -> choose an uncompleted random question -> render UI -> validate and normalize input -> update progress -> save JSON and export Excel -> render feedback.

## Project-specific conventions

- Configuration lives under the `Quiz` section in `appsettings.json`; keep new user-facing labels in `QuizTexts` so prompts and status messages remain configurable.
- Runtime file paths in config (`QuestionBankPath`, `ProgressPath`, `ExcelReportPath`) are resolved relative to `AppContext.BaseDirectory`, not the current working directory. The `.csproj` copies `appsettings.json` and `QuizPractice.tsv` to the output directory.
- Question IDs are derived as `$"{Type}-{Number}"`. Preserve this when changing progress storage because existing `quiz-progress.json` data depends on it.
- A question is completed when `CorrectCount > WrongCount`; question selection prioritizes unanswered, uncompleted questions before other uncompleted questions.
- TSV quiz files use columns in this order: type, number, text, A, B, C, D, correct answer. Empty options are valid, and trailing empty option columns may be omitted; the loader treats the last column as the correct answer.
- Multiple-choice answers are compared in normalized sorted-letter form, so `CA`, `A,C`, and `ac` can represent the same answer when valid for the question options.
- Console output is Chinese-first and uses Spectre.Console markup. Escape dynamic text with `Markup.Escape` before embedding it in markup strings.
- Progress JSON is written UTF-8 with indented JSON and relaxed escaping so Chinese text remains readable. Excel worksheet names and labels are also Chinese.
- `ProgressService.RecordAnswer` already saves progress; `Program.SaveAll` saves again before exporting the report. Be aware of this if refactoring persistence to avoid changing save timing unintentionally.
