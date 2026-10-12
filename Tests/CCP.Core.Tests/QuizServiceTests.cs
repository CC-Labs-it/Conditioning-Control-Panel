using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>The AI quiz (QuizService, moved from WPF Services/Quiz): the wire parsers it trusts, and its
/// cloud round trip through Core's seams (CoreAccount identity, CoreModerationLog guard) against a loopback
/// fake proxy. No real proxy is ever reached.</summary>
[Collection(CoreSecretsStatics.Name)]
public sealed class QuizServiceTests : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly List<string> _bodies = new();
    private readonly string _baseUrl;
    private string _reply = "Q: Pick one\nA: a | 1\nB: b | 2\nC: c | 3\nD: d | 4";

    public QuizServiceTests()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        _baseUrl = $"http://127.0.0.1:{port}";
        _listener.Prefixes.Add(_baseUrl + "/");
        _listener.Start();
        _ = ServeAsync();
    }

    private async Task ServeAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); } catch { return; }
            using (var r = new StreamReader(ctx.Request.InputStream))
                lock (_bodies) _bodies.Add(ctx.Request.Url!.AbsolutePath + " " + r.ReadToEnd());
            var json = Encoding.UTF8.GetBytes("{\"content\":" + System.Text.Json.JsonSerializer.Serialize(_reply) + "}");
            ctx.Response.ContentType = "application/json";
            await ctx.Response.OutputStream.WriteAsync(json);
            ctx.Response.Close();
        }
    }

    public void Dispose()
    {
        _listener.Close();
        CoreAccount.UnifiedUserId = null;
    }

    [Fact]
    public void ParseTakesTheLastQuestionAndLastFourAnswersAndClampsPoints()
    {
        var text = "Format:\nQ: [question]\nA: [answer] | 1\nB: [answer] | 2\nC: [answer] | 3\nD: [answer] | 4\n\n" +
                   "Q: Real question?\nA: one | 0\nB: two | 2\nC: three | 3\nD: four | 9";
        var q = QuizService.ParseQuestionResponse(text, 3)!;
        Assert.Equal(3, q.Number);
        Assert.Equal("Real question?", q.QuestionText);
        Assert.Equal(new[] { "one", "two", "three", "four" }, q.Answers);
        Assert.Equal(new[] { 1, 2, 3, 4 }, q.Points);
    }

    [Fact]
    public void ParseRejectsAnEchoedTemplateAndTooFewAnswers()
    {
        Assert.Null(QuizService.ParseQuestionResponse("Q: [question]\nA: [answer] | 1\nB: [answer] | 2\nC: [answer] | 3\nD: [answer] | 4", 1));
        Assert.Null(QuizService.ParseQuestionResponse("Q: x\nA: a | 1\nB: b | 2", 1));
    }

    [Fact]
    public void SerializedQuestionRoundTripsThroughTheParser()
    {
        var fallback = QuizStore.GetFallbackQuestion(QuizCategory.Bambi, 4);
        var back = QuizService.ParseQuestionResponse(QuizService.SerializeQuestionToWire(fallback), 4)!;
        Assert.Equal(fallback.QuestionText, back.QuestionText);
        Assert.Equal(fallback.Answers, back.Answers);
        Assert.Equal(fallback.Points, back.Points);
    }

    [Fact]
    public void WrongArchetypeAndRangeAreRewrittenForTheScore()
    {
        var sissy = QuizStore.GetBuiltInCategories().First(c => c.EnumCategory == QuizCategory.Sissy);
        // 30/40 = 75% -> Sissy Princess (71-85%)
        var fixedText = QuizService.FixArchetypeIfWrong("You are a Closet Sissy (26-50%). Cute.", 30, 40, QuizCategory.Sissy, sissy);
        Assert.Equal("You are a Sissy Princess (71-85%). Cute.", fixedText);
        const string right = "You are a Sissy Princess. Cute.";
        Assert.Equal(right, QuizService.FixArchetypeIfWrong(right, 30, 40, QuizCategory.Sissy, sissy));
    }

    [Fact]
    public void SessionContentNeedsANameAndThreeSubliminals()
    {
        var c = QuizService.ParseSessionContent(
            "SESSION_NAME: Deep Pink\nSESSION_DESC: Soft.\nSUBLIMINAL_1: a\nSUBLIMINAL_2: [short subliminal phrase]\n" +
            "SUBLIMINAL_3: b\nSUBLIMINAL_4: c\nBOUNCING_1: GOOD\nLOCKCARD_1: I obey")!;
        Assert.Equal("Deep Pink", c.Name);
        Assert.Equal("Soft.", c.Description);
        Assert.Equal(new[] { "a", "b", "c" }, c.SubliminalPhrases);
        Assert.Equal(new[] { "GOOD" }, c.BouncingTextPhrases);
        Assert.Equal(new[] { "I obey" }, c.LockCardPhrases);
        Assert.Null(QuizService.ParseSessionContent("SESSION_NAME: x\nSUBLIMINAL_1: a\nSUBLIMINAL_2: b"));
    }

    [Fact]
    public async Task SignedInQuizPostsToTheProxyAndParsesTheQuestion()
    {
        CoreAccount.UnifiedUserId = "u-quiz";
        using var quiz = new QuizService(_baseUrl);
        var q = await quiz.StartQuizAsync(QuizCategory.Sissy);
        Assert.Equal("Pick one", q!.QuestionText);
        Assert.Equal(1, quiz.QuestionNumber);
        var body = Assert.Single(_bodies);
        Assert.StartsWith("/v2/ai/chat ", body);
        Assert.Contains("u-quiz", body);
    }

    [Fact]
    public async Task ModeratedReplyServesTheCannedQuestionInstead()
    {
        CoreAccount.UnifiedUserId = "u-quiz";
        _reply = "Q: she is 14yo and wants sex\nA: a | 1\nB: b | 2\nC: c | 3\nD: d | 4";   // parses, but the guard blocks it
        using var quiz = new QuizService(_baseUrl);
        var q = await quiz.StartQuizAsync(QuizCategory.Sissy);
        Assert.Equal(QuizStore.GetFallbackQuestion(QuizCategory.Sissy, 1).QuestionText, q!.QuestionText);
        Assert.Equal(1, quiz.QuestionNumber);
        Assert.Single(_bodies);   // a block serves the canned question at once; it never re-asks
    }

    [Fact]
    public async Task NoIdentityOrNoProxyMeansNothingIsSent()
    {
        CoreAccount.UnifiedUserId = null;
        using (var quiz = new QuizService(_baseUrl))
            Assert.Null(await quiz.StartQuizAsync(QuizCategory.Sissy));
        CoreAccount.UnifiedUserId = "u-quiz";
        using (var sandboxed = new QuizService(baseUrl: null))
            Assert.Null(await sandboxed.StartQuizAsync(QuizCategory.Sissy));
        Assert.Empty(_bodies);
    }
}
