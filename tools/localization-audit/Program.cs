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
    $"Audited {entries.Count} localization source entries across "
        + $"{entries.Select(entry => entry.Path).Distinct(StringComparer.Ordinal).Count()} files."
);

if (failures.Count == 0)
{
    Console.WriteLine(
        "PASS: every launcher-authored visible Hangul literal has English and Simplified Chinese paths."
    );
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
    var mixedChinese = $"mod\"{externalKorean}\"发生错误.";
    EnglishLocalization.Register(mixedKorean, mixedEnglish);
    SimplifiedChineseLocalization.Register(mixedKorean, mixedEnglish, mixedChinese);

    Check(
        LocalizedTextPolicy.Render(
            launcherKorean,
            LauncherLanguage.English,
            TextProvenance.LauncherAuthored
        ) == launcherEnglish,
        "launcher-authored policy fixture did not translate"
    );
    Check(
        LocalizedTextPolicy.Render(
            "클라우드 받는 중... 7/225",
            LauncherLanguage.English,
            TextProvenance.LauncherAuthored
        ) == "Downloading cloud saves... 7/225",
        "dynamic cloud-pull progress did not translate"
    );
    Check(
        LocalizedTextPolicy.Render(
            "클라우드 정리 중... 3/20",
            LauncherLanguage.English,
            TextProvenance.LauncherAuthored
        ) == "Cleaning up cloud saves... 3/20",
        "dynamic cloud-cleanup progress did not translate"
    );
    Check(
        LocalizedTextPolicy.Render(
            launcherKorean,
            LauncherLanguage.English,
            TextProvenance.ExternalContent
        )
            == launcherKorean,
        "external text was rewritten even though it matched launcher copy"
    );
    Check(
        LocalizedTextPolicy.Render(
            mixedKorean,
            LauncherLanguage.English,
            TextProvenance.LauncherTemplateWithExternalContent
        ) == mixedEnglish,
        "mixed launcher/external pair did not translate"
    );
    Check(
        LocalizedTextPolicy.Render(
            mixedEnglish,
            LauncherLanguage.Korean,
            TextProvenance.LauncherTemplateWithExternalContent
        ) == mixedKorean,
        "mixed launcher/external pair did not round-trip"
    );
    Check(
        LocalizedTextPolicy.Render(
            mixedKorean,
            LauncherLanguage.SimplifiedChinese,
            TextProvenance.LauncherTemplateWithExternalContent
        ) == mixedChinese,
        "mixed launcher/external pair did not translate to Simplified Chinese"
    );
    Check(
        LocalizedTextPolicy.Render(
            mixedChinese,
            LauncherLanguage.Korean,
            TextProvenance.LauncherTemplateWithExternalContent
        ) == mixedKorean,
        "mixed Simplified Chinese launcher/external pair did not round-trip"
    );
    Check(
        LocalizedTextPolicy.Render(
            "Steam Username",
            LauncherLanguage.SimplifiedChinese,
            TextProvenance.LauncherAuthored
        ) == "Steam 用户名",
        "English-only launcher copy did not translate to Simplified Chinese"
    );
    Check(
        LocalizedTextPolicy.Render(
            "Welcome back, ExampleAccount",
            LauncherLanguage.SimplifiedChinese,
            TextProvenance.LauncherAuthored
        ) == "欢迎回来, ExampleAccount",
        "dynamic English template did not preserve its external account value"
    );
    Check(
        LocalizedTextPolicy.Render(
            "PLAY",
            LauncherLanguage.SimplifiedChinese,
            TextProvenance.LauncherAuthored
        ) == "开始游戏",
        "PLAY action was not translated"
    );
    Check(
        LocalizedTextPolicy.Render(
            "226686 subscriber(s) · 58.1 MB · 97% rated",
            LauncherLanguage.SimplifiedChinese,
            TextProvenance.LauncherAuthored
        ) == "226686 位订阅者 · 58.1 MB · 97% 好评",
        "Workshop dynamic statistics were not translated"
    );
    Check(
        LocalizedTextPolicy.Render(
            "131h 11m",
            LauncherLanguage.SimplifiedChinese,
            TextProvenance.LauncherAuthored
        ) == "131 小时 11 分钟",
        "dynamic playtime was not translated"
    );
    Check(
        LocalizedTextPolicy.Render(
            "198.0 KB · 131h 11m · 2막 6층",
            LauncherLanguage.SimplifiedChinese,
            TextProvenance.LauncherAuthored
        ) == "198.0 KB · 131 小时 11 分钟 · 第 2 幕, 第 6 层",
        "combined profile size/playtime/current-run template was not translated"
    );
    Check(
        LocalizedTextPolicy.Render(
            "[Cloud] SavePathCompat: GetRunSavePath → ≤v0.107 overload",
            LauncherLanguage.SimplifiedChinese,
            TextProvenance.LauncherDiagnosticWithExternalContent
        ) == "[云存档] 存档路径兼容性检查完成.",
        "visible Cloud diagnostic was not summarized in Simplified Chinese"
    );
    var punctuationSafe = SimplifiedChineseLocalization.ForDisplay(
        "点击“导入 mod（.zip）”，继续。"
    );
    Check(
        punctuationSafe.Contains("导入 mod(.zip)", StringComparison.Ordinal)
            && !punctuationSafe.Any(ch => "（）“”，。".Contains(ch)),
        "unsupported CJK punctuation was not normalized for the Android font"
    );
    Check(
        LocalizedTextPolicy.Render(
            "External English Workshop Title",
            LauncherLanguage.SimplifiedChinese,
            TextProvenance.ExternalContent
        ) == "External English Workshop Title",
        "external English content was rewritten in Simplified Chinese mode"
    );
    Check(
        SimplifiedChineseLocalization.LooksUntranslated("Untranslated launcher sentence")
            && !SimplifiedChineseLocalization.LooksUntranslated("启动器已就绪")
            && !SimplifiedChineseLocalization.LooksUntranslated("1.1 KB · —")
            && SimplifiedChineseLocalization.LooksUntranslated("1.1 KB download failed"),
        "runtime Simplified Chinese residue detector is not discriminating"
    );
    Check(
        LocalizedTextPolicy.IsUntranslatedLauncherText(
            "번역되지 않은 런처 문장",
            LauncherLanguage.English,
            TextProvenance.LauncherAuthored
        ),
        "unknown launcher-authored Hangul was silently accepted"
    );
    Check(
        !LocalizedTextPolicy.IsUntranslatedLauncherText(
            externalKorean,
            LauncherLanguage.English,
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
        "src/STS2Mobile/Launcher/LauncherController.cs",
        "TextProvenance.LauncherDiagnosticWithExternalContent"
    );
    RequireSource(
        "src/STS2Mobile/Launcher/Components/LogView.cs",
        "RefreshLanguage"
    );
    RequireSource(
        "src/STS2Mobile/Launcher/Components/LanguageToggle.cs",
        "untranslated={audit.UntranslatedLauncherText}"
    );
    RequireSource(
        "src/STS2Mobile/Launcher/LauncherController.cs",
        "_view.AppendLog(msg, TextProvenance.ExternalContent)"
    );
    RequireSource(
        "src/STS2Mobile/Launcher/LauncherController.cs",
        "_view.AppendLog(p.CurrentFile, TextProvenance.ExternalContent)"
    );
    RequireSource(
        "src/STS2Mobile/Launcher/Sections/ModManagerSection.cs",
        "Loc.Authored(\"WORKSHOP\")"
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
        if (
            relative.EndsWith("/StartupPerformanceStage.cs", StringComparison.Ordinal)
            && invocation.Expression.ToString() == "Stage"
            && invocation.ArgumentList.Arguments.Count >= 13
        )
        {
            var stageArguments = invocation.ArgumentList.Arguments;
            foreach (
                var (koreanIndex, englishIndex, chineseIndex) in new[]
                {
                    (2, 3, 4),
                    (5, 6, 7),
                }
            )
            {
                var koreanArgument = stageArguments[koreanIndex];
                var englishArgument = stageArguments[englishIndex];
                var chineseArgument = stageArguments[chineseIndex];
                var stageKorean = SampleExpression(koreanArgument.Expression);
                var stageEnglish = SampleExpression(englishArgument.Expression);
                var stageChinese = SampleExpression(chineseArgument.Expression);
                if (
                    !ContainsHangul(stageKorean)
                    || ContainsHangul(stageEnglish)
                    || string.IsNullOrWhiteSpace(stageEnglish)
                    || ContainsHangul(stageChinese)
                    || !ContainsCjk(stageChinese)
                    || ContainsTraditionalOnly(stageChinese)
                )
                {
                    failures.Add(
                        $"{relative}:{Line(tree, invocation.SpanStart)} invalid startup-stage Korean/English/zh-Hans trio"
                    );
                }
                else
                {
                    Add(
                        relative,
                        tree,
                        koreanArgument.SpanStart,
                        "ui-stage-catalog",
                        stageKorean
                    );
                }
                handledSpans.Add(koreanArgument.Span);
                handledSpans.Add(englishArgument.Span);
                handledSpans.Add(chineseArgument.Span);
            }
            continue;
        }

        if (!IsLocTr(invocation))
            continue;
        var arguments = invocation.ArgumentList.Arguments;
        if (
            IsLocSelect(invocation)
            && arguments.Count > 0
            && !ContainsHangul(SampleExpression(arguments[0].Expression))
        )
            continue;
        if (arguments.Count is < 2 or > 3)
        {
            failures.Add(
                $"{relative}:{Line(tree, invocation.SpanStart)} Loc.Tr must have two or three arguments"
            );
            continue;
        }

        var korean = SampleExpression(arguments[0].Expression);
        var english = SampleExpression(arguments[1].Expression);
        var chinese =
            arguments.Count == 3
                ? SampleExpression(arguments[2].Expression)
                : SimplifiedChineseLocalization.Translate(korean, english);
        if (
            !ContainsHangul(korean)
            || ContainsHangul(english)
            || string.IsNullOrWhiteSpace(english)
            || ContainsHangul(chinese)
            || !ContainsCjk(chinese)
            || ContainsTraditionalOnly(chinese)
        )
        {
            failures.Add(
                $"{relative}:{Line(tree, invocation.SpanStart)} invalid Loc.Tr Korean/English/zh-Hans path: "
                    + Display(korean)
            );
        }
        else
        {
            Add(relative, tree, invocation.SpanStart, "ui-explicit-pair", korean);
        }
        handledSpans.Add(arguments[0].Span);
        handledSpans.Add(arguments[1].Span);
        if (arguments.Count == 3)
            handledSpans.Add(arguments[2].Span);
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

        if (
            relative.EndsWith("/EnglishLocalization.cs", StringComparison.Ordinal)
            || relative.EndsWith(
                "/SimplifiedChineseLocalization.cs",
                StringComparison.Ordinal
            )
        )
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
            if (!HasAdjacentChinesePair(candidate))
            {
                failures.Add(
                    $"{relative}:{Line(tree, candidate.SpanStart)} adjacent Korean/English text lacks zh-Hans sibling: "
                        + Display(sample)
                );
            }
            continue;
        }

        var translated = EnglishLocalization.Translate(sample);
        var translatedChinese = SimplifiedChineseLocalization.Translate(sample, translated);
        Add(relative, tree, owner.SpanStart, "ui-central-overlay", sample);
        if (ContainsHangul(translated))
        {
            failures.Add(
                $"{relative}:{Line(tree, owner.SpanStart)} untranslated launcher text: "
                    + Display(sample)
            );
        }
        if (
            ContainsHangul(translatedChinese)
            || !ContainsCjk(translatedChinese)
            || ContainsTraditionalOnly(translatedChinese)
        )
        {
            failures.Add(
                $"{relative}:{Line(tree, owner.SpanStart)} missing Simplified Chinese launcher text: "
                    + Display(sample)
            );
        }
    }

    AuditEnglishLauncherText(relative, tree, root, handledSpans);
}

void AuditEnglishLauncherText(
    string relative,
    SyntaxTree tree,
    SyntaxNode root,
    List<TextSpan> handledSpans
)
{
    if (
        relative.EndsWith("/EnglishLocalization.cs", StringComparison.Ordinal)
        || relative.EndsWith("/SimplifiedChineseLocalization.cs", StringComparison.Ordinal)
        || relative.EndsWith("/LocalizedTextPolicy.cs", StringComparison.Ordinal)
        || relative.EndsWith("/LocalizedTextRegistry.cs", StringComparison.Ordinal)
    )
        return;

    var seen = new HashSet<TextSpan>();
    foreach (
        var candidate in root.DescendantNodes()
            .Where(node =>
                node is LiteralExpressionSyntax literal
                    && literal.IsKind(SyntaxKind.StringLiteralExpression)
                    && ContainsLatin(literal.Token.ValueText)
                || node is InterpolatedStringExpressionSyntax interpolated
                    && ContainsLatin(interpolated.ToString())
            )
    )
    {
        if (handledSpans.Any(span => span.Contains(candidate.Span)))
            continue;
        var owner = ExpandStringExpression(candidate);
        if (!seen.Add(owner.Span) || !IsLauncherUiText(owner))
            continue;
        var sample = SampleExpression(owner);
        if (
            string.IsNullOrWhiteSpace(sample)
            || ContainsHangul(sample)
            || !ContainsLatin(sample)
            || IsPlaceholderOnly(sample)
        )
            continue;
        if (IsApprovedUntranslatedToken(sample))
        {
            Add(relative, tree, owner.SpanStart, "ui-approved-token", sample);
            continue;
        }

        Add(relative, tree, owner.SpanStart, "ui-english-source", sample);
        var translated = SimplifiedChineseLocalization.Translate(sample, sample);
        if (
            translated == sample
            || !ContainsCjk(translated)
            || ContainsHangul(translated)
            || ContainsTraditionalOnly(translated)
        )
        {
            failures.Add(
                $"{relative}:{Line(tree, owner.SpanStart)} English launcher text lacks Simplified Chinese: "
                    + Display(sample)
            );
        }
    }
}

static bool IsLauncherUiText(SyntaxNode node)
{
    foreach (var ancestor in node.AncestorsAndSelf())
    {
        if (ancestor is ObjectCreationExpressionSyntax creation)
        {
            var type = creation.Type.ToString();
            if (type is "StyledLabel" or "StyledButton" or "StyledLineEdit")
            {
                if (
                    creation.ArgumentList?.Arguments.FirstOrDefault()?.Span.Contains(node.Span)
                    != true
                )
                    continue;
                if (
                    creation
                        .ToString()
                        .Contains("TextProvenance.ExternalContent", StringComparison.Ordinal)
                    || creation
                        .ToString()
                        .Contains(
                            "TextProvenance.LauncherTemplateWithExternalContent",
                            StringComparison.Ordinal
                        )
                )
                    return false;
                return true;
            }
        }

        if (ancestor is InvocationExpressionSyntax invocation)
        {
            var name = invocation.Expression.ToString();
            var arguments = invocation.ArgumentList.Arguments;
            var index = arguments.IndexOf(arguments.FirstOrDefault(argument =>
                argument.Span.Contains(node.Span)
            ));
            if (index < 0)
                continue;
            if (
                name.EndsWith(".AddItem", StringComparison.Ordinal)
                || name is "Ui.MakePill" or "Ui.MakeSectionHeader"
            )
                return index == 0;
            if (name == "Ui.MakeEmptyState")
                return index is 1 or 2;
            if (
                name
                    is "Loc.Authored"
                        or "SetStatus"
                        or "SetButtonText"
                        or "SetLaunchButtonText"
                        or "SetGameUpdateButtonText"
                        or "SetLauncherUpdateButtonText"
                        or "SetMessage"
                        or "ShowConfirmation"
                        or "AppendLog"
                        or "AppendColoredLog"
                || name.EndsWith(".SetStatus", StringComparison.Ordinal)
                || name.EndsWith(".SetButtonText", StringComparison.Ordinal)
                || name.EndsWith(".SetLaunchButtonText", StringComparison.Ordinal)
                || name.EndsWith(".SetGameUpdateButtonText", StringComparison.Ordinal)
                || name.EndsWith(".SetLauncherUpdateButtonText", StringComparison.Ordinal)
                || name.EndsWith(".ShowConfirmation", StringComparison.Ordinal)
                || name.EndsWith(".AppendLog", StringComparison.Ordinal)
                || name.EndsWith(".AppendColoredLog", StringComparison.Ordinal)
            )
                return index == 0;
        }

        if (
            ancestor is AssignmentExpressionSyntax assignment
            && assignment.Right.Span.Contains(node.Span)
        )
        {
            var target = assignment.Left.ToString();
            if (
                target.EndsWith(".Text", StringComparison.Ordinal)
                || target is "Text" or "PlaceholderText" or "TooltipText"
                || target.EndsWith(".PlaceholderText", StringComparison.Ordinal)
                || target.EndsWith(".TooltipText", StringComparison.Ordinal)
            )
                return true;
        }
    }
    return false;
}

static bool IsApprovedUntranslatedToken(string value) =>
    value
        is "StS2 Launcher"
            or "LANG"
            or "한국어"
            or "English"
            or "简体中文"
            or "public"
            or "Made using FMOD Studio by Firelight Technologies Pty Ltd.";

static bool IsPlaceholderOnly(string value)
{
    var stripped = Regex.Replace(value ?? "", "Sample|[0-9]|[\\s.·:/()%…-]", "");
    return stripped.Length == 0;
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
    if (
        string.IsNullOrWhiteSpace(english)
        || ContainsHangul(english)
        || relative.EndsWith("/SimplifiedChineseLocalization.cs", StringComparison.Ordinal)
            && ContainsTraditionalOnly(english)
    )
    {
        failures.Add(
            $"{relative}:{Line(tree, candidate.SpanStart)} catalog entry lacks an English pair: "
                + Display(sample)
        );
    }
        if (
            relative.EndsWith("/EnglishLocalization.cs", StringComparison.Ordinal)
            && (ContainsHangul(SimplifiedChineseLocalization.Translate(sample, english))
                || !ContainsCjk(SimplifiedChineseLocalization.Translate(sample, english)))
            && !ChineseCatalogContains(sample)
        )
    {
        failures.Add(
            $"{relative}:{Line(tree, candidate.SpanStart)} catalog entry lacks Simplified Chinese: "
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
        var languageValues =
            koreanIndex >= 0
                ? paired
                .Skip(koreanIndex + 1)
                .Where(item => !string.IsNullOrWhiteSpace(item.Value))
                .Select(item => item.Value)
                .ToArray()
                : Array.Empty<string>();
        var hasEnglishAfter = languageValues.Any(value =>
            !ContainsHangul(value) && !ContainsCjk(value)
        );
        var hasChineseAfter =
            languageValues.Any(value => !ContainsHangul(value) && ContainsCjk(value));
        hasChineseAfter =
            hasChineseAfter
            && languageValues.Any(value =>
                !ContainsHangul(value)
                && ContainsCjk(value)
                && !ContainsTraditionalOnly(value)
            );
        entries.Add(
            new InventoryEntry(relative, literal.Line, "android-native-pair", literal.Value)
        );
        if (!hasEnglishAfter || !hasChineseAfter)
        {
            failures.Add(
                $"{relative}:{literal.Line} nativeText Korean argument lacks English/zh-Hans paths"
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
    invocation.Expression.ToString()
        is "Loc.Tr"
            or "Loc.Select"
            or "STS2Mobile.Launcher.Components.Loc.Tr"
            or "STS2Mobile.Launcher.Components.Loc.Select";

static bool IsLocSelect(InvocationExpressionSyntax invocation) =>
    invocation.Expression.ToString()
        is "Loc.Select" or "STS2Mobile.Launcher.Components.Loc.Select";

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

static bool HasAdjacentChinesePair(SyntaxNode node)
{
    var korean = node.AncestorsAndSelf().OfType<VariableDeclaratorSyntax>().FirstOrDefault();
    if (korean == null || !korean.Identifier.ValueText.EndsWith("Ko", StringComparison.Ordinal))
        return false;
    var chineseName = korean.Identifier.ValueText[..^2] + "Zh";
    var scope = korean
        .Ancestors()
        .FirstOrDefault(ancestor => ancestor is BlockSyntax or ArrowExpressionClauseSyntax);
    if (scope == null)
        return false;
    var chinese = scope
        .DescendantNodes()
        .OfType<VariableDeclaratorSyntax>()
        .FirstOrDefault(variable => variable.Identifier.ValueText == chineseName);
    if (chinese?.Initializer == null)
        return false;
    var source = chinese.Initializer.Value.ToString();
    return ContainsCjk(source) && !ContainsHangul(source);
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

static bool ContainsCjk(string value) =>
    !string.IsNullOrEmpty(value) && value.Any(ch => ch is >= '\u3400' and <= '\u9FFF');

static bool ContainsTraditionalOnly(string value)
{
    const string traditionalOnly = "檔雲設啟體顯載錄錯誤開關網頁選擇進態碼號這個從將與為後裡過還請時區據級擊戶權現";
    return !string.IsNullOrEmpty(value) && value.Any(traditionalOnly.Contains);
}

static bool ContainsLatin(string value) =>
    !string.IsNullOrEmpty(value) && value.Any(ch => ch is >= 'A' and <= 'Z' or >= 'a' and <= 'z');

static string Display(string value)
{
    var compact = Regex.Replace(value ?? "", "\\s+", " ").Trim();
    return compact.Length <= 140 ? compact : compact[..137] + "...";
}

bool ChineseCatalogContains(string sample)
{
    var path = Path.Combine(
        repository,
        "src",
        "STS2Mobile",
        "Launcher",
        "Components",
        "SimplifiedChineseLocalization.cs"
    );
    return File.ReadAllText(path).Contains(sample, StringComparison.Ordinal);
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
