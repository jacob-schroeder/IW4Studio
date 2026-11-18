using System.Xml;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;

namespace IW4.Studio.Desktop.Editors.Shader;

internal static class CgSyntaxHighlighting
{
    private static readonly Lazy<IHighlightingDefinition> LightDefinition = new(() => LoadDefinition(dark: false));
    private static readonly Lazy<IHighlightingDefinition> DarkDefinition = new(() => LoadDefinition(dark: true));

    public static IHighlightingDefinition ForTheme(bool dark) =>
        dark ? DarkDefinition.Value : LightDefinition.Value;

    private static IHighlightingDefinition LoadDefinition(bool dark)
    {
        string definition = $$"""
            <SyntaxDefinition name="Cg" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
              <Color name="Comment" foreground="{{(dark ? "#A4A9B2" : "#626972")}}" fontStyle="italic" />
              <Color name="String" foreground="{{(dark ? "#A9CB85" : "#3F6D26")}}" />
              <Color name="Literal" foreground="{{(dark ? "#B1C985" : "#516628")}}" />
              <Color name="Keyword" foreground="{{(dark ? "#F3AE76" : "#984713")}}" fontWeight="bold" />
              <Color name="Type" foreground="{{(dark ? "#84D8CC" : "#166B62")}}" />
              <Color name="Function" foreground="{{(dark ? "#F1D28A" : "#795A13")}}" />
              <Color name="Semantic" foreground="{{(dark ? "#C8B2F2" : "#69419A")}}" />
              <Color name="Swizzle" foreground="{{(dark ? "#9ACCFD" : "#245C96")}}" />
              <Color name="Directive" foreground="{{(dark ? "#E8B0D1" : "#8E356E")}}" />
              <Color name="Error" foreground="{{(dark ? "#FFA0A0" : "#AB2525")}}" fontWeight="bold" />
              <RuleSet ignoreCase="false">
                <Span color="Comment" begin="//" />
                <Span color="Comment" multiline="true" begin="/\*" end="\*/" />
                <Span color="String" begin="&quot;" end="&quot;">
                  <RuleSet>
                    <Span begin="\\" end="." />
                  </RuleSet>
                </Span>

                <Span color="Error" begin="^[ \t]*\#[ \t]*error\b" />
                <Rule color="Directive">^[ \t]*\#[ \t]*(?:define|undef|if|ifdef|ifndef|elif|else|endif|include|line|pragma)\b</Rule>

                <Rule color="Keyword">
                  \b(?:asm|break|case|class|column_major|const|continue|default|discard|do|else|extern|for|if|in|inline|inout|
                       interface|out|packed|return|row_major|static|struct|switch|typedef|uniform|varying|volatile|while)\b
                </Rule>
                <Rule color="Type">
                  \b(?:(?:bool|char|double|fixed|float|half|int|long|short|unsigned)(?:[1-4](?:x[1-4])?)?|
                       sampler(?:1D|2D|3D|CUBE|RECT)?|string|void)\b
                </Rule>

                <!-- Semantics are case-insensitive. Restrict register names to binding positions. -->
                <Rule color="Semantic">
                  :[ \t]*(?i:attr(?:ib)?\d+|binormal\d*|blendindices\d*|blendweight\d*|bcol\d*|col(?:or)?\d*|depth\d*|
                             face|fog(?:coord)?\d*|normal\d*|position\d*|psize|tangent\d*|texcoord\d*|texunit\d+|wpos|c\d+)\b
                  | :[ \t]*register[ \t]*\([ \t]*[cs]\d+[ \t]*\)
                </Rule>

                <Rule color="Function">
                  \b(?:abs|acos|all|any|asin|atan2?|ceil|clamp|clip|cosh?|cross|ddx|ddy|degrees|determinant|distance|dot|
                       exp2?|floor|fmod|frac|frexp|fwidth|isfinite|isinf|isnan|ldexp|length|lerp|lit|log(?:2|10)?|main|
                       max|min|modf|mul|normalize|pow|radians|reflect|refract|round|rsqrt|saturate|sign|sinh?|smoothstep|
                       sqrt|step|tanh?|tex(?:1D|2D|3D|CUBE|RECT)(?:bias|lod|proj)?|transpose|trunc)\b(?=[ \t]*\()
                </Rule>
                <Rule color="Swizzle">\.(?:[xyzw]{1,4}|[rgba]{1,4}|(?:_m[0-3][0-3]){1,4}|(?:_[1-4][1-4]){1,4})\b</Rule>
                <Rule color="Literal">
                  \b(?:false|true)\b
                  | (?&lt;![\w.])(?:0[xX][0-9a-fA-F]+(?:[iIlLsStT]|[uU][iIlLsStT]?)?
                                | (?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?(?:[dDfFhHiIlLsStTxX]|[uU][iIlLsStT]?)?)(?!\w)
                </Rule>
              </RuleSet>
            </SyntaxDefinition>
            """;
        using XmlReader reader = XmlReader.Create(new StringReader(definition));
        return HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }
}
