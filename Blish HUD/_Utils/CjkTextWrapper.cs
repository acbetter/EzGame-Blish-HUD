using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Blish_HUD {

    /// <summary>
    /// Greedy Unicode-aware line wrapping for UI labels. It preserves ordinary
    /// Latin words while allowing line breaks between CJK text elements and
    /// keeping common opening/closing punctuation off invalid line edges.
    /// </summary>
    internal static class CjkTextWrapper {

        private const string OPENING_PUNCTUATION = "([{（［｛〈《「『【〔〖〘〚“‘";
        private const string CLOSING_PUNCTUATION = ")]}）］｝〉》」』】〕〗〙〛，、。．！？；：％‰‱…‥·・:;,.!?%";

        public static string Wrap(string text, float maxLineWidth, Func<string, float> measure) {
            if (string.IsNullOrEmpty(text) || maxLineWidth <= 0 || measure == null) return text ?? string.Empty;

            List<string> elements = GetTextElements(text);
            if (elements.Count == 0) return string.Empty;

            var widths = new float[elements.Count];
            for (int i = 0; i < elements.Count; i++) {
                widths[i] = measure(elements[i]);
            }

            var result = new StringBuilder(text.Length + elements.Count / 8);
            int lineStart = 0;

            while (lineStart < elements.Count) {
                while (lineStart < elements.Count && IsWhitespace(elements[lineStart])) lineStart++;
                if (lineStart >= elements.Count) break;

                float lineWidth = 0;
                int lastBreak = -1;
                int index = lineStart;

                for (; index < elements.Count; index++) {
                    if (index > lineStart && CanBreakBefore(elements[index - 1], elements[index])) {
                        lastBreak = index;
                    }

                    if (lineWidth + widths[index] > maxLineWidth && index > lineStart) {
                        // When a punctuation pair alone is wider than the line,
                        // slightly overflow instead of producing an invalid line
                        // beginning/ending with detached punctuation.
                        if (lastBreak <= lineStart &&
                            (IsClosingPunctuation(elements[index]) || IsOpeningPunctuation(elements[index - 1]))) {
                            lineWidth += widths[index];
                            continue;
                        }

                        int lineEnd = lastBreak > lineStart ? lastBreak : index;
                        AppendLine(result, elements, lineStart, lineEnd);
                        lineStart = lineEnd;
                        break;
                    }

                    lineWidth += widths[index];
                }

                if (index >= elements.Count) {
                    AppendLine(result, elements, lineStart, elements.Count);
                    break;
                }
            }

            return result.ToString();
        }

        private static List<string> GetTextElements(string text) {
            var elements = new List<string>(text.Length);
            TextElementEnumerator enumerator = StringInfo.GetTextElementEnumerator(text);

            while (enumerator.MoveNext()) {
                elements.Add((string)enumerator.Current);
            }

            return elements;
        }

        private static void AppendLine(StringBuilder result, IReadOnlyList<string> elements, int start, int end) {
            while (end > start && IsWhitespace(elements[end - 1])) end--;
            if (result.Length > 0) result.Append('\n');

            for (int i = start; i < end; i++) {
                result.Append(elements[i]);
            }
        }

        private static bool CanBreakBefore(string previous, string current) {
            if (IsWhitespace(previous)) return true;
            if (IsWhitespace(current)) return false;
            if (IsOpeningPunctuation(previous) || IsClosingPunctuation(current)) return false;

            return IsCjk(previous) || IsCjk(current) || previous == "-" || previous == "/";
        }

        private static bool IsWhitespace(string element) {
            return !string.IsNullOrEmpty(element) && char.IsWhiteSpace(element, 0);
        }

        private static bool IsOpeningPunctuation(string element) {
            return element.Length == 1 && OPENING_PUNCTUATION.IndexOf(element[0]) >= 0;
        }

        private static bool IsClosingPunctuation(string element) {
            return element.Length == 1 && CLOSING_PUNCTUATION.IndexOf(element[0]) >= 0;
        }

        private static bool IsCjk(string element) {
            if (string.IsNullOrEmpty(element)) return false;

            int codePoint = char.ConvertToUtf32(element, 0);
            return codePoint >= 0x2E80 && codePoint <= 0x9FFF
                || codePoint >= 0xF900 && codePoint <= 0xFAFF
                || codePoint >= 0x3040 && codePoint <= 0x30FF
                || codePoint >= 0xAC00 && codePoint <= 0xD7AF
                || codePoint >= 0xFF00 && codePoint <= 0xFFEF;
        }
    }
}
