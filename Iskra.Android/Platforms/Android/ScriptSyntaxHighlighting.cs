using System.Text.RegularExpressions;
using Android.Graphics;
using Android.Text;
using Android.Text.Style;
using Android.Widget;
using System.Runtime.CompilerServices;

namespace Iskra.Android;

internal static class ScriptSyntaxHighlighting
{
    private static readonly ConditionalWeakTable<EditText, SyntaxWatcher> Watchers = new();
    private static readonly int CommandColor = global::Android.Graphics.Color.ParseColor("#E7A27C");
    private static readonly int PropertyColor = global::Android.Graphics.Color.ParseColor("#8FC1AD");
    private static readonly int LiteralColor = global::Android.Graphics.Color.ParseColor("#E8C67D");
    private static readonly int NumberColor = global::Android.Graphics.Color.ParseColor("#B6A0D4");
    private static readonly int CommentColor = global::Android.Graphics.Color.ParseColor("#85817B");
    private static readonly Regex Commands = new(
        @"(?i)\b(?:при|когда|если|если|иначеесли|иначеэсли|иначеесле|иначе|конец|вечно|пока|прервать|задать|присвоить|изменить|поместить|подвинуть|печать|показать|спрятать|скрыть|ждать|воспроизвести|зациклить|остановить|проиграть|спросить|запустить|продолжить|сменить|случайное|значение)\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex Properties = new(@"(?i)свойство\.[а-яё]+", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex Numbers = new(@"(?<![\p{L}\p{N}_])[+-]?(?:\d+(?:[.,]\d*)?|[.,]\d+)", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex Strings = new("\"(?:\\\\.|[^\"\\\\])*\"", RegexOptions.Compiled);
    private static readonly Regex Comments = new("//.*$", RegexOptions.Multiline | RegexOptions.Compiled);

    public static void Attach(EditText editor)
    {
        _ = Watchers.GetValue(editor, nativeEditor =>
        {
            var watcher = new SyntaxWatcher();
            nativeEditor.AddTextChangedListener(watcher);
            watcher.AfterTextChanged(nativeEditor.EditableText);
            return watcher;
        });
    }

    private sealed class SyntaxWatcher : Java.Lang.Object, ITextWatcher
    {
        private readonly List<ForegroundColorSpan> spans = [];

        public void BeforeTextChanged(Java.Lang.ICharSequence? s, int start, int count, int after) { }
        public void OnTextChanged(Java.Lang.ICharSequence? s, int start, int before, int count) { }

        public void AfterTextChanged(IEditable? editable)
        {
            if (editable is null) return;
            foreach (var span in spans) editable.RemoveSpan(span);
            spans.Clear();
            var source = editable.ToString();
            var protectedRanges = new List<(int Start, int End)>();
            Apply(Strings, LiteralColor, protect: true);
            Apply(Comments, CommentColor, protect: true);
            Apply(Commands, CommandColor);
            Apply(Properties, PropertyColor);
            Apply(Numbers, NumberColor);

            void Apply(Regex pattern, int color, bool protect = false)
            {
                foreach (Match match in pattern.Matches(source))
                {
                    if (!protect && protectedRanges.Any(range => match.Index < range.End && match.Index + match.Length > range.Start)) continue;
                    if (protect) protectedRanges.Add((match.Index, match.Index + match.Length));
                    var span = new ForegroundColorSpan(new global::Android.Graphics.Color(color));
                    editable.SetSpan(span, match.Index, match.Index + match.Length, SpanTypes.ExclusiveExclusive);
                    spans.Add(span);
                }
            }
        }
    }
}
