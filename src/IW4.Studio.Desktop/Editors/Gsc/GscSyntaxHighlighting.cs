using System.Xml;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;

namespace IW4.Studio.Desktop.Editors.Gsc;

/// <summary>
/// Rider Darcula-inspired presentation of the token set recovered in
/// <c>IW4.Gsc.Syntax.GscTokenKind</c>. The definition deliberately keeps the
/// scanner's lowercase-only reserved words case-sensitive.
/// </summary>
internal static class GscSyntaxHighlighting
{
    private static readonly Lazy<IHighlightingDefinition> LazyDefinition = new(
        LoadDefinition);

    public static IHighlightingDefinition Definition => LazyDefinition.Value;

    private static IHighlightingDefinition LoadDefinition()
    {
        using XmlReader reader = XmlReader.Create(new StringReader(DefinitionXml));
        return HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }

    private const string DefinitionXml = """
        <SyntaxDefinition name="IW4 GSC" extensions=".gsc;.csc" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
          <Color name="Comment" foreground="#808080" fontStyle="italic" />
          <Color name="String" foreground="#6A8759" />
          <Color name="Number" foreground="#6897BB" />
          <Color name="Keyword" foreground="#CC7832" fontWeight="bold" />
          <Color name="Predefined" foreground="#9876AA" />
          <Color name="Directive" foreground="#CC7832" fontWeight="bold" />
          <Color name="Path" foreground="#FFC66D" />
          <Color name="Animation" foreground="#6A9FB5" />
          <Color name="Special" foreground="#C0A9D9" />
          <RuleSet ignoreCase="false">
            <!-- IW4 comments: non-nesting block comments and line comments. -->
            <Span color="Comment" begin="//" end="$" />
            <Span color="Comment" multiline="true" begin="/\*" end="\*/" />

            <!-- The scanner accepts ordinary and localized strings. -->
            <Span color="String" begin="&amp;&quot;" end="&quot;" escapeCharacter="\\" />
            <Span color="String" begin="&quot;" end="&quot;" escapeCharacter="\\" />

            <!-- Exact IW4 source directives and developer delimiters. -->
            <Rule color="Directive">\#include\b|\#using_animtree\b|\#animtree\b</Rule>
            <Rule color="Directive">/\#|\#/</Rule>

            <!-- A script path is identifier segments joined by backslashes. -->
            <Rule color="Path">(?&lt;![A-Za-z0-9_])[A-Za-z_][A-Za-z0-9_]*(?:\\[A-Za-z_][A-Za-z0-9_]*)+(?![A-Za-z0-9_])</Rule>

            <!-- Float exponents are uppercase E only; hexadecimal literals do not exist. -->
            <Rule color="Number">(?&lt;![A-Za-z_])(?:\d+(?:\.\d+)?E[+-]?\d+|\d+\.\d+|\.\d+|\d+)</Rule>

            <Rule color="Animation">%(?=[A-Za-z_])</Rule>
            <Rule color="Special">::|\$|\.size\b</Rule>

            <Keywords color="Keyword">
              <Word>return</Word>
              <Word>wait</Word>
              <Word>thread</Word>
              <Word>childthread</Word>
              <Word>call</Word>
              <Word>if</Word>
              <Word>else</Word>
              <Word>while</Word>
              <Word>for</Word>
              <Word>foreach</Word>
              <Word>in</Word>
              <Word>waittill</Word>
              <Word>waittillmatch</Word>
              <Word>waittillframeend</Word>
              <Word>notify</Word>
              <Word>switch</Word>
              <Word>case</Word>
              <Word>default</Word>
              <Word>break</Word>
              <Word>continue</Word>
              <Word>endon</Word>
              <Word>breakpoint</Word>
              <Word>prof_begin</Word>
              <Word>prof_end</Word>
              <Word>breakon</Word>
            </Keywords>

            <Keywords color="Predefined">
              <Word>undefined</Word>
              <Word>self</Word>
              <Word>thisthread</Word>
              <Word>level</Word>
              <Word>game</Word>
              <Word>anim</Word>
              <Word>true</Word>
              <Word>false</Word>
            </Keywords>
          </RuleSet>
        </SyntaxDefinition>
        """;
}
