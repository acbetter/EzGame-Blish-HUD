using Blish_HUD;
using System;
using System.Globalization;
using System.Linq;

internal static class Program {

    private static readonly Func<string, float> Monospace = value => new StringInfo(value).LengthInTextElements * 10;

    public static int Main() {
        VerifyChineseSentenceWrapsWithoutLosingCharacters();
        VerifyCjkPunctuationStaysOnValidLineEdges();
        VerifyEnglishWordsRemainIntact();
        Console.WriteLine("Text wrapping contract tests passed.");
        return 0;
    }

    private static void VerifyChineseSentenceWrapsWithoutLosingCharacters() {
        const string text = "使用“症状配装（流血）”和消耗品，可以让这次冒险稍微轻松一些。";
        string wrapped = CjkTextWrapper.Wrap(text, 100, Monospace);

        Assert(wrapped.Contains("\n"), "Chinese sentence was not wrapped.");
        Assert(wrapped.Replace("\n", string.Empty) == text, "Wrapping lost or reordered Chinese characters.");
        Assert(wrapped.Split('\n').All(line => Monospace(line) <= 100), "A Chinese line exceeds the requested width.");
    }

    private static void VerifyCjkPunctuationStaysOnValidLineEdges() {
        const string opening = "([{（［｛〈《「『【〔〖〘〚“‘";
        const string closing = ")]}）］｝〉》」』】〕〗〙〛，、。．！？；：％‰‱…‥·・:;,.!?%";
        string wrapped = CjkTextWrapper.Wrap("这是《中文换行》测试，可以正确处理标点。", 60, Monospace);

        foreach (string line in wrapped.Split('\n')) {
            Assert(line.Length == 0 || closing.IndexOf(line[0]) < 0, $"Closing punctuation starts a line: {line}");
            Assert(line.Length == 0 || opening.IndexOf(line[line.Length - 1]) < 0, $"Opening punctuation ends a line: {line}");
        }

        Assert(CjkTextWrapper.Wrap("字，", 10, Monospace) == "字，", "Closing punctuation was detached on a narrow line.");
        Assert(CjkTextWrapper.Wrap("《中", 10, Monospace) == "《中", "Opening punctuation was detached on a narrow line.");
    }

    private static void VerifyEnglishWordsRemainIntact() {
        string wrapped = CjkTextWrapper.Wrap("Pathing keeps English words intact", 130, Monospace);
        Assert(!wrapped.Contains("Path\ning"), "An English word was split despite fitting on a line.");
    }

    private static void Assert(bool condition, string message) {
        if (!condition) throw new InvalidOperationException(message);
    }
}
