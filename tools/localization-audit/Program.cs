using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using STS2Mobile.Launcher.Components;

var repository = FindRepositoryRoot();
var verbose = args.Contains("--list", StringComparer.Ordinal);
var entries = new List<InventoryEntry>();
var failures = new List<string>();

AuditPolicyFixtures();
AuditProvenanceContracts();

foreach (
    var path in Directory
        .EnumerateFiles(
            Path.Combine(repository, "src", "STS2Mobile"),
            "*.cs",
            SearchOption.AllDirectories
        )
        .OrderBy(path => path, StringComparer.Ordinal)
)
{
    AuditCSharp(path);
}

foreach (
    var path in Directory
        .EnumerateFiles(
            Path.Combine(repository, "android", "src"),
            "*.java",
            SearchOption.AllDirectories
        )
        .OrderBy(path => path, StringComparer.Ordinal)
)
{
    AuditJava(path);
}

foreach (
    var path in Directory
        .EnumerateFiles(
            Path.Combine(repository, "android", "res"),
            "*.xml",
            SearchOption.AllDirectories
        )
        .OrderBy(path => path, StringComparer.Ordinal)
)
{
    AuditXml(path);
}

if (verbose)
{
    foreach (var entry in entries.OrderBy(entry => entry.Path).ThenBy(entry => entry.Line))
        Console.WriteLine(
            $"{entry.Path}:{entry.Line}\t{entry.Classification}\t{Display(entry.Sample)}"
        );
}

foreach (
    var group in entries
        .GroupBy(entry => entry.Classification)
        .OrderBy(group => group.Key, StringComparer.Ordinal)
)
{
    Console.WriteLine($"{group.Key}: {group.Count()}");
}

Console.WriteLine(
    $"Audited {entries.Count} Hangul-bearing source entries across "
        + $"{entries.Select(entry => entry.Path).Distinct(StringComparer.Ordinal).Count()} files."
);

if (failures.Count == 0)
{
    Console.WriteLine("PASS: every launcher-authored visible Hangul literal has an English path.");
    return 0;
}

foreach (var failure in failures)
    Console.Error.WriteLine($"FAIL {failure}");
Console.Error.WriteLine($"Localization audit failed with {failures.Count} issue(s).");
return 1;

void AuditPolicyFixtures()
{
    const string launcherKorean = "클라우드 백업 중";
    const string launcherEnglish = "Backing up to cloud";
    const string externalKorean = "외부 한글 모드 이름";
    var mixedKorean = $"'{externalKorean}' 모드에서 오류가 발생했습니다.";
    var mixedEnglish = $"The '{externalKorean}' mod encountered an error.";
    EnglishLocalization.Register(mixedKorean, mixedEnglish);

    Check(
        LocalizedTextPolicy.Render(
            launcherKorean,
            useEnglish: true,
            TextProvenance.LauncherAuthored
        ) == launcherEnglish,
        "launcher-authored policy fixture did not translate"
    );
    Check(
        LocalizedTextPolicy.Render(launcherKorean, useEnglish: true, TextProvenance.ExternalContent)
            == launcherKorean,
        "external text was rewritten even though it matched launcher copy"
    );
    Check(
        LocalizedTextPolicy.Render(
            mixedKorean,
            useEnglish: true,
            TextProvenance.LauncherTemplateWithExternalContent
        ) == mixedEnglish,
        "mixed launcher/external pair did not translate"
    );
    Check(
        LocalizedTextPolicy.Render(
            mixedEnglish,
            useEnglish: false,
            TextProvenance.LauncherTemplateWithExternalContent
        ) == mixedKorean,
        "mixed launcher/external pair did not round-trip"
    );
    Check(
        LocalizedTextPolicy.IsUntranslatedLauncherText(
            "번역되지 않은 런처 문장",
            TextProvenance.LauncherAuthored
        ),
        "unknown launcher-authored Hangul was silently accepted"
    );
    Check(
        !LocalizedTextPolicy.IsUntranslatedLauncherText(
            externalKorean,
            TextProvenance.ExternalContent
        ),
        "external Hangul was classified as a launcher localization failure"
    );
}

void AuditProvenanceContracts()
{
    RequireSource(
        "src/STS2Mobile/Launcher/Components/ModListRow.cs",
        "provenance: TextProvenance.ExternalContent"
    );
    RequireSource(
        "src/STS2Mobile/Launcher/Components/WorkshopBrowseCard.cs",
        "provenance: TextProvenance.ExternalContent"
    );
    RequireSource(
        "src/STS2Mobile/Launcher/Components/WorkshopDetailPage.cs",
        "TextProvenance.LauncherTemplateWithExternalContent"
    );
    RequireSource(
        "src/STS2Mobile/Launcher/LauncherView.cs",
        "TextProvenance.LauncherTemplateWithExternalContent"
    );
    RequireSource(
        "src/STS2Mobile/Launcher/Components/LanguageToggle.cs",
        "authored_hangul={audit.UntranslatedLauncherText}"
    );
}

void RequireSource(string relative, string required)
{
    var path = Path.Combine(repository, relative.Replace('/', Path.DirectorySeparatorChar));
    Check(
        File.Exists(path) && File.ReadAllText(path).Contains(required, StringComparison.Ordinal),
        $"{relative} lacks provenance/audit contract: {required}"
    );
}

void Check(bool condition, string message)
{
    if (!condition)
        failures.Add($"policy: {message}");
}

void AuditCSharp(string path)
{
    var source = File.ReadAllText(path);
    var relative = Relative(path);
    var tree = CSharpSyntaxTree.ParseText(source, path: relative);
    var root = tree.GetRoot();

    foreach (
        var comment in root.DescendantTrivia(descendIntoTrivia: true)
            .Where(trivia =>
                trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
                || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia)
                || trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
                || trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia)
            )
            .Where(trivia => ContainsHangul(trivia.ToString()))
    )
    {
        Add(relative, tree, comment.SpanStart, "non-ui-comment", comment.ToString());
    }

    var handledSpans = new List<TextSpan>();
    foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
    {
        if (!IsLocTr(invocation))
            continue;
        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count != 2)
        {
            failures.Add(
                $"{relative}:{Line(tree, invocation.SpanStart)} Loc.Tr must have two arguments"
            );
            continue;
        }

        var korean = SampleExpression(arguments[0].Expression);
        var english = SampleExpression(arguments[1].Expression);
        if (
            !ContainsHangul(korean)
            || ContainsHangul(english)
            || string.IsNullOrWhiteSpace(english)
        )
        {
            failures.Add(
                $"{relative}:{Line(tree, invocation.SpanStart)} invalid Loc.Tr Korean/English pair"
            );
        }
        else
        {
            Add(relative, tree, invocation.SpanStart, "ui-explicit-pair", korean);
        }
        handledSpans.Add(arguments[0].Span);
        handledSpans.Add(arguments[1].Span);
    }

    var candidates = root.DescendantNodes()
        .Where(node =>
            node is LiteralExpressionSyntax literal
                && literal.IsKind(SyntaxKind.StringLiteralExpression)
                && ContainsHangul(literal.Token.ValueText)
            || node is InterpolatedStringExpressionSyntax interpolated
                && ContainsHangul(interpolated.ToString())
        )
        .ToArray();
    var seenOwners = new HashSet<TextSpan>();
    foreach (var candidate in candidates)
    {
        if (handledSpans.Any(span => span.Contains(candidate.Span)))
            continue;
        var owner = ExpandStringExpression(candidate);
        if (!seenOwners.Add(owner.Span))
            continue;
        var sample = SampleExpression(owner);
        if (!ContainsHangul(sample))
            continue;

        if (relative.EndsWith("/EnglishLocalization.cs", StringComparison.Ordinal))
        {
            AuditCatalogEntry(relative, tree, candidate, sample);
            continue;
        }
        if (IsLogOnly(candidate))
        {
            Add(relative, tree, candidate.SpanStart, "non-ui-log", sample);
            continue;
        }
        if (HasAdjacentEnglishPair(candidate))
        {
            Add(relative, tree, candidate.SpanStart, "ui-adjacent-pair", sample);
            continue;
        }

        var translated = EnglishLocalization.Translate(sample);
        Add(relative, tree, owner.SpanStart, "ui-central-overlay", sample);
        if (ContainsHangul(translated))
        {
            failures.Add(
                $"{relative}:{Line(tree, owner.SpanStart)} untranslated launcher text: "
                    + Display(sample)
            );
        }
    }
}

void AuditCatalogEntry(string relative, SyntaxTree tree, SyntaxNode candidate, string sample)
{
    string english = null;
    var assignment = candidate
        .AncestorsAndSelf()
        .OfType<AssignmentExpressionSyntax>()
        .FirstOrDefault();
    if (assignment != null && assignment.Left.Span.Contains(candidate.Span))
        english = SampleExpression(assignment.Right);

    var invocation = candidate
        .AncestorsAndSelf()
        .OfType<InvocationExpressionSyntax>()
        .FirstOrDefault();
    if (
        english == null
        && invocation?.Expression.ToString() == "Make"
        && invocation.ArgumentList.Arguments.Count == 2
        && invocation.ArgumentList.Arguments[0].Span.Contains(candidate.Span)
    )
        english = SampleExpression(invocation.ArgumentList.Arguments[1].Expression);

    var tuple = candidate.AncestorsAndSelf().OfType<TupleExpressionSyntax>().FirstOrDefault();
    if (
        english == null
        && tuple?.Arguments.Count == 2
        && tuple.Arguments[0].Span.Contains(candidate.Span)
    )
        english = SampleExpression(tuple.Arguments[1].Expression);

    Add(relative, tree, candidate.SpanStart, "translation-catalog", sample);
    if (string.IsNullOrWhiteSpace(english) || ContainsHangul(english))
    {
        failures.Add(
            $"{relative}:{Line(tree, candidate.SpanStart)} catalog entry lacks an English pair: "
                + Display(sample)
        );
    }
}

void AuditJava(string path)
{
    var source = File.ReadAllText(path);
    var relative = Relative(path);
    var scan = ScanQuotedLanguage(source);
    foreach (var comment in scan.Comments.Where(item => ContainsHangul(item.Value)))
        entries.Add(new InventoryEntry(relative, comment.Line, "non-ui-comment", comment.Value));

    foreach (var literal in scan.Strings.Where(item => ContainsHangul(item.Value)))
    {
        var invocation = FindContainingInvocation(source, literal.Offset, "nativeText");
        if (invocation == null)
        {
            entries.Add(
                new InventoryEntry(relative, literal.Line, "unclassified-java", literal.Value)
            );
            failures.Add(
                $"{relative}:{literal.Line} Android-visible Hangul must use nativeText(ko, en): "
                    + Display(literal.Value)
            );
            continue;
        }

        var paired = ScanQuotedLanguage(invocation).Strings;
        var koreanIndex = paired.FindIndex(item => ContainsHangul(item.Value));
        var hasEnglishAfter =
            koreanIndex >= 0
            && paired
                .Skip(koreanIndex + 1)
                .Any(item => !ContainsHangul(item.Value) && !string.IsNullOrWhiteSpace(item.Value));
        entries.Add(
            new InventoryEntry(relative, literal.Line, "android-native-pair", literal.Value)
        );
        if (!hasEnglishAfter)
        {
            failures.Add(
                $"{relative}:{literal.Line} nativeText Korean argument lacks an English pair"
            );
        }
    }
}

void AuditXml(string path)
{
    var source = File.ReadAllText(path);
    var relative = Relative(path);
    foreach (
        Match comment in Regex.Matches(source, "<!--[\\s\\S]*?-->", RegexOptions.CultureInvariant)
    )
    {
        if (ContainsHangul(comment.Value))
            entries.Add(
                new InventoryEntry(
                    relative,
                    SourceLine(source, comment.Index),
                    "non-ui-comment",
                    comment.Value
                )
            );
        source = source
            .Remove(comment.Index, comment.Length)
            .Insert(comment.Index, new string(' ', comment.Length));
    }
    if (ContainsHangul(source))
    {
        failures.Add($"{relative}: Android XML contains unpaired visible Hangul");
        entries.Add(new InventoryEntry(relative, 1, "unclassified-xml", source));
    }
}

static SyntaxNode ExpandStringExpression(SyntaxNode node)
{
    var current = node;
    while (
        current.Parent is ParenthesizedExpressionSyntax
        || current.Parent is BinaryExpressionSyntax binary
            && binary.IsKind(SyntaxKind.AddExpression)
    )
        current = current.Parent;
    return current;
}

static string SampleExpression(SyntaxNode node)
{
    switch (node)
    {
        case LiteralExpressionSyntax literal
            when literal.IsKind(SyntaxKind.StringLiteralExpression):
            return literal.Token.ValueText;
        case ParenthesizedExpressionSyntax parenthesized:
            return SampleExpression(parenthesized.Expression);
        case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.AddExpression):
            return SampleExpression(binary.Left) + SampleExpression(binary.Right);
        case InterpolatedStringExpressionSyntax interpolated:
            var builder = new StringBuilder();
            foreach (var content in interpolated.Contents)
            {
                if (content is InterpolatedStringTextSyntax text)
                    builder.Append(text.TextToken.ValueText);
                else if (content is InterpolationSyntax interpolation)
                    builder.Append(Placeholder(interpolation.Expression.ToString()));
            }
            return builder.ToString();
        default:
            return "Sample";
    }
}

static string Placeholder(string expression)
{
    var lower = expression.ToLowerInvariant();
    if (
        lower.Contains("suffix", StringComparison.Ordinal)
        || lower.Contains("moddedtag", StringComparison.Ordinal)
        || expression.Contains(" · 모드", StringComparison.Ordinal)
    )
        return " · 모드";
    foreach (
        var numeric in new[]
        {
            "count",
            "done",
            "total",
            "bytes",
            "length",
            "size",
            "remaining",
            "pending",
            "index",
            "profile",
            "wins",
            "losses",
            "act",
            "floor",
            "processed",
            "shader",
            ".id",
        }
    )
    {
        if (lower.Contains(numeric, StringComparison.Ordinal))
            return "1";
    }
    return "Sample";
}

static bool IsLocTr(InvocationExpressionSyntax invocation) =>
    invocation.Expression.ToString() is "Loc.Tr" or "STS2Mobile.Launcher.Components.Loc.Tr";

static bool IsLogOnly(SyntaxNode node)
{
    foreach (var invocation in node.AncestorsAndSelf().OfType<InvocationExpressionSyntax>())
    {
        var name = invocation.Expression.ToString();
        if (
            name
                is "PatchHelper.Log"
                    or "GD.Print"
                    or "GD.PrintErr"
                    or "GD.PushError"
                    or "GD.PushWarning"
                    or "Console.Write"
                    or "Console.WriteLine"
                    or "Console.Error.WriteLine"
            || name.EndsWith(".Log", StringComparison.Ordinal)
        )
            return true;
    }
    return false;
}

static bool HasAdjacentEnglishPair(SyntaxNode node)
{
    var korean = node.AncestorsAndSelf().OfType<VariableDeclaratorSyntax>().FirstOrDefault();
    if (korean == null || !korean.Identifier.ValueText.EndsWith("Ko", StringComparison.Ordinal))
        return false;
    var englishName = korean.Identifier.ValueText[..^2] + "En";
    var scope = korean
        .Ancestors()
        .FirstOrDefault(ancestor => ancestor is BlockSyntax or ArrowExpressionClauseSyntax);
    if (scope == null)
        return false;
    var english = scope
        .DescendantNodes()
        .OfType<VariableDeclaratorSyntax>()
        .FirstOrDefault(variable => variable.Identifier.ValueText == englishName);
    if (english?.Initializer == null)
        return false;
    var source = english.Initializer.Value.ToString();
    return !ContainsHangul(source) && source.Contains('"');
}

static string FindContainingInvocation(string source, int offset, string method)
{
    var search = offset;
    while (search >= 0)
    {
        var name = source.LastIndexOf(method + "(", search, StringComparison.Ordinal);
        if (name < 0)
            return null;
        var open = name + method.Length;
        var close = FindMatchingParenthesis(source, open);
        if (close >= offset)
            return source.Substring(name, close - name + 1);
        search = name - 1;
    }
    return null;
}

static int FindMatchingParenthesis(string source, int open)
{
    var depth = 0;
    var inString = false;
    var escaped = false;
    for (var i = open; i < source.Length; i++)
    {
        var ch = source[i];
        if (inString)
        {
            if (escaped)
                escaped = false;
            else if (ch == '\\')
                escaped = true;
            else if (ch == '"')
                inString = false;
            continue;
        }
        if (ch == '"')
        {
            inString = true;
            continue;
        }
        if (ch == '(')
            depth++;
        else if (ch == ')' && --depth == 0)
            return i;
    }
    return -1;
}

static LanguageScan ScanQuotedLanguage(string source)
{
    var strings = new List<ScannedText>();
    var comments = new List<ScannedText>();
    var line = 1;
    for (var i = 0; i < source.Length; )
    {
        if (source[i] == '\n')
        {
            line++;
            i++;
            continue;
        }
        if (i + 1 < source.Length && source[i] == '/' && source[i + 1] == '/')
        {
            var start = i;
            var startLine = line;
            i += 2;
            while (i < source.Length && source[i] != '\n')
                i++;
            comments.Add(new ScannedText(start, startLine, source[start..i]));
            continue;
        }
        if (i + 1 < source.Length && source[i] == '/' && source[i + 1] == '*')
        {
            var start = i;
            var startLine = line;
            i += 2;
            while (i + 1 < source.Length && !(source[i] == '*' && source[i + 1] == '/'))
            {
                if (source[i] == '\n')
                    line++;
                i++;
            }
            i = Math.Min(source.Length, i + 2);
            comments.Add(new ScannedText(start, startLine, source[start..i]));
            continue;
        }
        if (source[i] != '"')
        {
            i++;
            continue;
        }

        var literalStart = i;
        var literalLine = line;
        var value = new StringBuilder();
        i++;
        var escaped = false;
        while (i < source.Length)
        {
            var ch = source[i++];
            if (ch == '\n')
                line++;
            if (escaped)
            {
                value.Append(
                    ch switch
                    {
                        'n' => '\n',
                        'r' => '\r',
                        't' => '\t',
                        _ => ch,
                    }
                );
                escaped = false;
            }
            else if (ch == '\\')
                escaped = true;
            else if (ch == '"')
                break;
            else
                value.Append(ch);
        }
        strings.Add(new ScannedText(literalStart, literalLine, value.ToString()));
    }
    return new LanguageScan(strings, comments);
}

void Add(string path, SyntaxTree tree, int offset, string classification, string sample) =>
    entries.Add(new InventoryEntry(path, Line(tree, offset), classification, sample));

string Relative(string path) =>
    Path.GetRelativePath(repository, path).Replace(Path.DirectorySeparatorChar, '/');

static int Line(SyntaxTree tree, int offset) =>
    tree.GetLineSpan(new TextSpan(offset, 0)).StartLinePosition.Line + 1;

static int SourceLine(string source, int offset) =>
    1 + source.Take(Math.Clamp(offset, 0, source.Length)).Count(ch => ch == '\n');

static bool ContainsHangul(string value) =>
    !string.IsNullOrEmpty(value) && value.Any(ch => ch is >= '\uAC00' and <= '\uD7A3');

static string Display(string value)
{
    var compact = Regex.Replace(value ?? "", "\\s+", " ").Trim();
    return compact.Length <= 140 ? compact : compact[..137] + "...";
}

static string FindRepositoryRoot()
{
    var current = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (current != null)
    {
        if (
            File.Exists(Path.Combine(current.FullName, "GOAL_STABILITY_HARDENING.md"))
            && Directory.Exists(Path.Combine(current.FullName, "src", "STS2Mobile"))
        )
            return current.FullName;
        current = current.Parent;
    }
    throw new DirectoryNotFoundException("Repository root not found.");
}

internal sealed record InventoryEntry(string Path, int Line, string Classification, string Sample);

internal sealed record ScannedText(int Offset, int Line, string Value);

internal sealed record LanguageScan(List<ScannedText> Strings, List<ScannedText> Comments);
