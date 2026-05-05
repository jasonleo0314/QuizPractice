using QuizPractice.Web;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile(
    Path.Combine(AppContext.BaseDirectory, "appsettings.json"),
    optional: false,
    reloadOnChange: false);

builder.Services.AddSingleton<QuizWebSession>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/state", (QuizWebSession session) => Results.Ok(session.GetState()));
app.MapPost("/api/answer", (AnswerRequest request, QuizWebSession session) => Results.Ok(session.SubmitAnswer(request.Answer)));
app.MapPost("/api/next", (QuizWebSession session) => Results.Ok(session.NextQuestion()));
app.MapPost("/api/exit", (QuizWebSession session) => Results.Ok(session.Exit()));
app.MapGet("/api/report", (QuizWebSession session) =>
{
    var report = session.GetReport();
    return Results.File(
        report.Content,
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        report.FileName);
});

app.Run();
