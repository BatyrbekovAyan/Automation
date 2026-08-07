using System.Collections.Generic;
using Automation.BotSettingsUI;
using NUnit.Framework;

public class PromptTextComposerTests
{
    private const string Line = "Отвечай коротко, до 2 предложений";
    private const string Other = "Обращайся к клиенту на «вы»";

    [Test]
    public void Append_ToEmptyPrompt_HasNoLeadingNewline()
    {
        Assert.AreEqual(Line, PromptTextComposer.Append("", Line));
    }

    [Test]
    public void Append_ToWhitespaceOnlyPrompt_HasNoLeadingNewline()
    {
        Assert.AreEqual(Line, PromptTextComposer.Append("   \n\n", Line));
    }

    [Test]
    public void Append_ToPromptWithoutTrailingNewline_InsertsExactlyOne()
    {
        Assert.AreEqual($"Базовый текст\n{Line}",
            PromptTextComposer.Append("Базовый текст", Line));
    }

    [Test]
    public void Append_ToPromptWithTrailingBlankLines_CollapsesThem()
    {
        Assert.AreEqual($"Базовый текст\n{Line}",
            PromptTextComposer.Append("Базовый текст\n\n\n", Line));
    }

    [Test]
    public void Append_AlreadyPresentLine_LeavesPromptUnchanged()
    {
        var prompt = $"Базовый текст\n{Line}";
        Assert.AreEqual(prompt, PromptTextComposer.Append(prompt, Line));
    }

    [Test]
    public void Contains_IsLineExact_NotSubstring()
    {
        // The stored line merely STARTS with the needle — it is a different instruction.
        var prompt = "Отвечай коротко, до 2 предложений и по делу";
        Assert.IsFalse(PromptTextComposer.Contains(prompt, "Отвечай коротко"));
    }

    [Test]
    public void Contains_IgnoresSurroundingWhitespaceOnStoredLine()
    {
        Assert.IsTrue(PromptTextComposer.Contains($"  {Line}  ", Line));
    }

    [Test]
    public void Remove_FromMiddle_LeavesNeighboursOnConsecutiveLines()
    {
        var prompt = $"Первая\n{Line}\nПоследняя";
        Assert.AreEqual("Первая\nПоследняя", PromptTextComposer.Remove(prompt, Line));
    }

    [Test]
    public void Remove_DropsEveryCopyOfTheLine()
    {
        var prompt = $"{Line}\nСередина\n{Line}";
        Assert.AreEqual("Середина", PromptTextComposer.Remove(prompt, Line));
    }

    [Test]
    public void Remove_AbsentLine_LeavesPromptUnchanged()
    {
        Assert.AreEqual("Базовый текст", PromptTextComposer.Remove("Базовый текст", Line));
    }

    [Test]
    public void CarriageReturns_NormaliseToNewlines()
    {
        Assert.AreEqual($"Первая\n{Line}",
            PromptTextComposer.Append("Первая\r\n", Line));
    }

    [Test]
    public void AppendThenRemove_RoundTripsToTrimmedOriginal()
    {
        const string prompt = "Базовый текст\n";
        var round = PromptTextComposer.Remove(PromptTextComposer.Append(prompt, Line), Line);
        Assert.AreEqual(prompt.TrimEnd(), round);
    }

    [Test]
    public void ApplyDiff_RemovesBeforeAdding_AndKeepsAddOrder()
    {
        var prompt = $"Базовый текст\n{Other}";
        var result = PromptTextComposer.ApplyDiff(
            prompt,
            toAdd: new List<string> { Line, "Третья строка" },
            toRemove: new List<string> { Other });
        Assert.AreEqual($"Базовый текст\n{Line}\nТретья строка", result);
    }
}
